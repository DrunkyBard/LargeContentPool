using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace LargeContentPool
{
	public sealed class Content : IDisposable, IEnumerable<byte>
	{
		private readonly LargeContentPool _pool;
		private readonly LinkedList<ByteArraySegment> _chunks;
		private bool _disposed;

		internal Content(LargeContentPool pool)
		{
			_pool = pool;
			_chunks = new LinkedList<ByteArraySegment>();
		}

		internal Content(LargeContentPool pool, ByteArraySegment initialSegment)
		{
			_pool = pool;
			_chunks = new LinkedList<ByteArraySegment>();
			_chunks.AddLast(initialSegment);
		}

		public Content Write(byte data)
		{
			CheckIfCanPerformOperation();

			var lastSegment = FetchLastSegment();

			if (lastSegment.CanWrite(1L))
			{
				lastSegment.Write(data);
			}
			else
			{
				var newSegment = _pool.NextSegment(); 
				newSegment.Write(data);
				_chunks.AddLast(newSegment);
			}

			return this;
		}

		public Content Write(byte[] data, int offset, int count)
		{
			CheckIfCanPerformOperation();

			var lastSegment = FetchLastSegment();

			if (lastSegment.CanWrite(count))
			{
				lastSegment.Write(data, offset, count);
			}
			else
			{
				var written = lastSegment.Free;
				lastSegment.Write(data, offset, lastSegment.Free);

				foreach (var (newSegment, filled) in Segments(count - written))
				{
					newSegment.Write(data, written, filled);
					_chunks.AddLast(newSegment);
					written += filled;
				}
			}

			return this;
		}

		public Content Write(Stream stream)
		{
			if (!stream.CanRead)
			{
				throw new ArgumentException("Stream should be readable");
			}

			var lastSegment = FetchLastSegment();

			if (lastSegment.Free == 0)
			{
				lastSegment = _pool.NextSegment();
				_chunks.AddLast(lastSegment);
			}

			int readed;

			while ((readed = stream.Read(lastSegment.Array, lastSegment.Offset, lastSegment.Free)) > 0)
			{
				lastSegment.SetFilled(readed);

				if (lastSegment.Free == 0)
				{
					lastSegment = _pool.NextSegment();
					_chunks.AddLast(lastSegment);
				}
			}

			return this;
		}

		public Content Read(Stream stream)
		{
			if (!stream.CanWrite)
			{
				throw new ArgumentException("Stream should be writeable");
			}

			foreach (var chunk in _chunks)
			{
				stream.Write(chunk.Array, chunk.Offset, chunk.AvailableCount);
			}

			return this;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private ByteArraySegment FetchLastSegment()
		{
			if (_chunks.Count == 0)
			{
				_chunks.AddLast(_pool.NextSegment());
			}

			return _chunks.Last.Value;
		}

		private IEnumerable<ValueTuple<ByteArraySegment, int>> Segments(int count)
		{
			do
			{
				var newSegment = _pool.NextSegment();
				var filled = !newSegment.CanWrite(count) ? newSegment.AvailableCount : count;
				count = count - filled;

				yield return (newSegment, filled);
			} while (count != 0);
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			while (_chunks.Count != 0)
			{
				var nextChunk = _chunks.Last.Value;
				_chunks.RemoveLast();
				_pool.Release(nextChunk);
			}

			_disposed = true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void CheckIfCanPerformOperation()
		{
			if (_disposed)
			{
				throw new ObjectDisposedException(typeof(Content).Name);
			}
		}

		public IEnumerator<byte> GetEnumerator() => new ContentEnumerator(_chunks.GetEnumerator());

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		private sealed class ContentEnumerator : IEnumerator<byte>
		{
			private readonly IEnumerator<ByteArraySegment> _segmentsEnumerator;
			private IEnumerator<byte> _currentSegmentEnumerator;

			public ContentEnumerator(LinkedList<ByteArraySegment>.Enumerator segmentsEnumerator)
			{
				_segmentsEnumerator = segmentsEnumerator;
			}

			public bool MoveNext()
			{
				if (_currentSegmentEnumerator != null && _currentSegmentEnumerator.MoveNext())
				{
					return true;
				}

				if (!_segmentsEnumerator.MoveNext())
				{
					return false;
				}

				var currentEnumerator = _segmentsEnumerator.Current.GetEnumerator();

				while (!currentEnumerator.MoveNext())
				{
					if (!_segmentsEnumerator.MoveNext())
					{
						return false;
					}

					currentEnumerator = _segmentsEnumerator.Current.GetEnumerator();
				}

				_currentSegmentEnumerator = currentEnumerator;

				return true;
			}

			public byte Current => _currentSegmentEnumerator.Current;

			object IEnumerator.Current => Current;

			public void Reset()
			{
				throw new NotSupportedException();
			}

			public void Dispose()
			{
				_segmentsEnumerator.Dispose();
				_currentSegmentEnumerator?.Dispose();
			}
		}
	}
}
