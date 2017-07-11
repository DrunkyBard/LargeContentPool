﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LargeContentPool
{
	public sealed class Content : IDisposable, IEnumerable<byte>
	{
		private readonly LargeContentPool _pool;
		private readonly LinkedList<ByteArraySegment> _chunks;
		private bool _disposed;
		private bool _recalculateSize;
		private int _size;

		public long Size {
			get
			{
				if (!_recalculateSize)
				{
					return _size;
				}

				_size = _chunks.Aggregate(0, (size, chunk) => size + chunk.Filled);
				_recalculateSize = false;

				return _size;
			}
		}

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
			_recalculateSize = true;
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

			_recalculateSize = true;
		}

		public void ReadFrom(byte[] data, int offset, int count)
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

			_recalculateSize = true;
		}

		public void ReadFrom(Stream stream)
		{
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

				if (lastSegment.Free == 0)
				{
					lastSegment = _pool.NextSegment();
					_chunks.AddLast(lastSegment);
				}
			}

			_recalculateSize = true;
		}

		public async Task ReadFromAsync(Stream stream, CancellationToken token)
		{
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

				if (lastSegment.Free == 0)
				{
					lastSegment = _pool.NextSegment();
					_chunks.AddLast(lastSegment);
				}
			}

			_recalculateSize = true;
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

			_recalculateSize = true;
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
