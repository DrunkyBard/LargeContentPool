using System;
using System.Collections;
using System.Collections.Generic;

namespace LargeContentPool
{
	internal sealed class ByteArraySegment : IEnumerable<byte>
	{
		public readonly int AvailableCount;
		public readonly byte[] Array;
		public readonly int Offset;
		public int Filled { get; private set; }

		public int Free => AvailableCount - Filled;

		public ByteArraySegment(byte[] array, int offset, int count)
		{
			AvailableCount = count;
			Array = array;
			Offset = offset;
			Filled = 0;
		}

		internal bool CanWrite(long count) => count <= Free;

		internal void SetFilled(int count) => Filled = count;

		internal void Write(byte data)
		{
			if (!CanWrite(1L))
			{
				throw new InvalidOperationException("Cannot increase segment to 1 byte");
			}

			Array[Offset + Filled] = data;
			Filled++;
		}

		internal void Write(byte[] array, int offset, int count)
		{
			if (!CanWrite(count))
			{
				throw new InvalidOperationException($"Cannot increase segment to {count} bytes");
			}

			int internalArrayOffset = Offset + Filled;

			for (int i = offset; i < offset + count; i++)
			{
				Array[internalArrayOffset] = array[i];
				internalArrayOffset++;
			}

			Filled += count;
		}

		public IEnumerator<byte> GetEnumerator() => new SegmentEnumerator(this);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		private sealed class SegmentEnumerator : IEnumerator<byte>
		{
			private readonly ByteArraySegment _segment;
			private long _currentIdx;

			public SegmentEnumerator(ByteArraySegment segment)
			{
				_segment = segment;
				_currentIdx = segment.Offset-1;
			}

			public bool MoveNext()
			{
				if (++_currentIdx < _segment.Offset + _segment.Filled)
				{
					return true;
				}

				return false;
			}

			public void Reset()
			{
				throw new NotSupportedException();
			}

			public byte Current => _segment.Array[_currentIdx];

			object IEnumerator.Current => Current;

			public void Dispose()
			{ }
		}
	}
}
