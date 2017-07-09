using System;
using System.Collections.Generic;
using System.Linq;

namespace LargeContentPool
{
	public sealed class LargeContentPool
	{
		private const int DefaultChunkSize = 85_000;
		private LinkedList<byte[]> _memory;
		private readonly bool _bounded;
		private readonly int _chunkSize;
		private readonly LinkedList<ByteArraySegment> _free;
		private readonly long _initialSize;

		public long Size => _memory.Aggregate(0L, (size, bytes) => size + bytes.LongLength);

		public LargeContentPool(int initialSize, bool bounded) : this(initialSize, DefaultChunkSize, bounded)
		{ }

		public LargeContentPool(int initialSize, int chunkSize, bool bounded)
		{
			if (initialSize <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(initialSize), "Initial size should be greater than zero");
			}

			_memory = new LinkedList<byte[]>();
			_memory.AddLast(new byte[initialSize]);
			_initialSize = initialSize;
			_bounded = bounded;
			_chunkSize = Math.Min(chunkSize, initialSize);
			_free = new LinkedList<ByteArraySegment>();
			InitializeFreeSegments();
		}

		private void InitializeFreeSegments()
		{
			var lastSegment = _memory.Last.Value;

			for (int i = 0; i < lastSegment.LongLength; i += _chunkSize)
			{
				var segment = new ByteArraySegment(lastSegment, i, Math.Min(_chunkSize, lastSegment.Length - i));
				_free.AddLast(segment);
			}
		}

		public Content Acquire() => new Content(this, NextSegment());

		internal void Release(ByteArraySegment releasedSegment)
		{
			Array.Clear(releasedSegment.Array, releasedSegment.Offset, releasedSegment.Filled);
			_free.AddFirst(releasedSegment);
		}

		public void ForceIncrease()
		{
			var newMemory = new byte[_initialSize];
			_memory.AddLast(newMemory);
			InitializeFreeSegments();
		}

		internal ByteArraySegment NextSegment() //TODO: handle bounded flag
		{
			if (_free.Count == 0)
			{
				ForceIncrease();
			}

			var next = _free.First.Value;
			_free.RemoveFirst();

			return next;
		}
	}
}
