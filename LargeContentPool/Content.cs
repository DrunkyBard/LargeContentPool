using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LargeContentPool
{
	public sealed class Content : IDisposable, IEnumerable<byte>
	{
		private readonly LargeContentPool _pool;
		private readonly LinkedList<ByteArraySegment> _chunks;
		private bool _disposed;

		public long Size { get; private set; }

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

		public void ReadFrom(byte data)
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

			Size += 1;
		}

		public void ReadFrom(byte[] data, int offset, int count)
		{
			if (data == null)
			{
				throw new ArgumentNullException(nameof(data));
			}

			if (offset < 0 || offset >= data.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(offset), "Offset should be non negative and less than source array length");
			}

			if (data.Length - offset < count)
			{
				throw new ArgumentOutOfRangeException(nameof(count), "Offset + count is greater than source array length");
			}

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

			Size += count;
		}

		public void ReadFrom(Stream stream)
		{
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}

			CheckIfCanPerformOperation();

			if (!stream.CanRead)
			{
				throw new ArgumentException("Stream should be readable");
			}

			var lastSegment = FetchLastSegment();
			int readed;
			
			while ((readed = stream.Read(lastSegment.Array, lastSegment.Offset, lastSegment.Free)) > 0)
			{
				lastSegment.SetFilled(readed);
				Size += readed;

				if (lastSegment.Free == 0)
				{
					lastSegment = _pool.NextSegment();
					_chunks.AddLast(lastSegment);
				}
			}
		}

		public async Task ReadFromAsync(Stream stream, CancellationToken token)
		{
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}

			CheckIfCanPerformOperation();

			if (!stream.CanRead)
			{
				throw new ArgumentException("Stream should be readable");
			}

			token.ThrowIfCancellationRequested();
			var lastSegment = FetchLastSegment();
			int readed;

			while ((readed = await stream.ReadAsync(lastSegment.Array, lastSegment.Offset, lastSegment.Free, token)) > 0)
			{
				lastSegment.SetFilled(readed);
				Size += readed;

				if (lastSegment.Free == 0)
				{
					lastSegment = _pool.NextSegment();
					_chunks.AddLast(lastSegment);
				}
			}
		}

		private ByteArraySegment FetchLastSegment()
		{
			if (_chunks.Count == 0)
			{
				_chunks.AddLast(_pool.NextSegment());

				return _chunks.Last.Value;
			}

			var lastSegment = _chunks.Last.Value;

			if (lastSegment.Free == 0)
			{
				lastSegment = _pool.NextSegment();
				_chunks.AddLast(lastSegment);
			}

			return lastSegment;
		}

		public void WriteTo(Stream stream)
		{
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}

			CheckIfCanPerformOperation();

			if (!stream.CanWrite)
			{
				throw new ArgumentException("Stream should be writeable");
			}

			foreach (var chunk in _chunks)
			{
				stream.Write(chunk.Array, chunk.Offset, chunk.AvailableCount);
			}
		}

		public async Task WriteToAsync(Stream stream, CancellationToken token)
		{
			if (stream == null)
			{
				throw new ArgumentNullException(nameof(stream));
			}

			CheckIfCanPerformOperation();

			if (!stream.CanWrite)
			{
				throw new ArgumentException("Stream should be writeable");
			}

			foreach (var chunk in _chunks)
			{
				await stream.WriteAsync(chunk.Array, chunk.Offset, chunk.AvailableCount, token);
			}
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

		public void Clear()
		{
			foreach (var chunk in _chunks)
			{
				Array.Clear(chunk.Array, chunk.Offset, chunk.Filled);
			}
		}

		public void Resize(int newSize)
		{
			if (newSize < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(newSize), "New size should be greater than zero");
			}

			if (Size == newSize)
			{
				return;
			}

			if (Size > newSize)
			{
				Shrink(newSize);
			}
			else
			{
				Increase(newSize);
			}

			Size = newSize;
		}

		private void Increase(int size)
		{
			var delta = size - Size;

			do
			{
				var newSegment = _pool.NextSegment();
				_chunks.AddLast(newSegment);
				delta -= newSegment.AvailableCount;
			} while (delta > 0);
		}

		private void Shrink(int size)
		{
			var chunk = _chunks.First;

			do
			{
				if (chunk == null)
				{
					return;
				}

				size -= chunk.Value.AvailableCount;
				chunk = chunk.Next;
			} while (size > 0);

			if (chunk != null)
			{
				while (chunk != null)
				{
					_chunks.Remove(chunk);
					_pool.Release(chunk.Value);
					chunk = chunk.Next;
				}
			}
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
