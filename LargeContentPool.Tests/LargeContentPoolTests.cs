using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace LargeContentPool.Tests
{
	public sealed class LargeContentPoolTests
	{
		[Theory]
		[InlineData(100), InlineData(1000)]
		public void WhenWriteSingleBytes_ThenAllBytesShouldBeWritesSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var buffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte)(x % byte.MaxValue)).ToArray();

			foreach (var b in bytes)
			{
				buffer.ReadFrom(b);
			}

			Assert.Equal(bytes, buffer);
			Assert.Equal(bytes.LongLength, buffer.Size);
		}

		[Theory]
		[InlineData(100), InlineData(1000)]
		public void WhenWriteCollectionOfBytes_ThenAllBytesShouldBeWritesSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var buffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte)(x % byte.MaxValue)).ToArray();
			buffer.ReadFrom(bytes, 0, bytes.Length);

			Assert.Equal(bytes, buffer);
			Assert.Equal(bytes.LongLength, buffer.Size);
		}

		[Theory]
		[InlineData(100), InlineData(1000)]
		public async Task WhenWriteDataFromStream_ThenAllBytesShouldBeWritesSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var syncBuffer = pool.Acquire();
			var asyncBuffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte)(x % byte.MaxValue)).ToArray();

			using (var memStream = new MemoryStream())
			{
				memStream.Write(bytes, 0, bytes.Length);
				memStream.Position = 0;
				syncBuffer.ReadFrom(memStream);
			}

			using (var memStream = new MemoryStream())
			{
				memStream.Write(bytes, 0, bytes.Length);
				memStream.Position = 0;
				await asyncBuffer.ReadFromAsync(memStream, CancellationToken.None);
			}

			Assert.Equal(bytes, syncBuffer);
			Assert.Equal(bytes.LongLength, syncBuffer.Size);
			Assert.Equal(bytes, asyncBuffer);
			Assert.Equal(bytes.LongLength, asyncBuffer.Size);
		}

		[Theory]
		[InlineData(100), InlineData(1000)]
		public async Task WhenWriteDataFromBufferToStream_ThenAllBytesShouldBeWritesToStreamSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var syncBuffer = pool.Acquire();
			var asyncBuffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte)(x % byte.MaxValue)).ToArray();
			syncBuffer.ReadFrom(bytes, 0, bytes.Length);
			asyncBuffer.ReadFrom(bytes, 0, bytes.Length);
			byte[] syncMemStreamArray;
			byte[] asyncMemStreamArray;

			using (var memStream = new MemoryStream())
			{
				syncBuffer.WriteTo(memStream);
				syncMemStreamArray = memStream.ToArray();
			}

			using (var memStream = new MemoryStream())
			{
				await asyncBuffer.WriteToAsync(memStream, CancellationToken.None);
				asyncMemStreamArray = memStream.ToArray();
			}

			Assert.Equal(bytes, syncMemStreamArray);
			Assert.Equal(bytes, asyncMemStreamArray);
		}

		[Fact]
		public void WhenPerformInducedForcedIncrease_ThenPoolSizeShouldBeIncreasedOnInitialSize()
		{
			var r = new Random();
			var initialSize = r.Next(1000, 2000);
			var pool = new LargeContentPool(initialSize, 10, false);

			pool.ForceIncrease();

			Assert.Equal(initialSize * 2, pool.Total);
		}

		[Fact]
		public void WhenReleaseBuffer_ThenAllMemoryShouldBeReturnedToPool()
		{
			var r = new Random();
			var initialSize = r.Next(1000, 2000);
			const int chunkSize = 10;
			var content = Enumerable.Range(0, chunkSize).Select(i => (byte) i).ToArray();
			var pool = new LargeContentPool(initialSize, chunkSize, false);
			var buffers = new List<Content>();

			while (pool.Free > chunkSize)
			{
				var buf = pool.Acquire();
				buf.ReadFrom(content, 0, content.Length);
				buffers.Add(buf);
			}

			var allocatedBytes = buffers.Aggregate(0L, (count, buf) => count + buf.Size);
			var poolAcquired = pool.Total - pool.Free;

			foreach (var buffer in buffers)
			{
				buffer.Dispose();
			}

			Assert.True(poolAcquired == allocatedBytes, $"Pool acquired size '{poolAcquired}' is not equal allocated size '{allocatedBytes}'");
			Assert.True(pool.Free == pool.Total, $"All buffers has release, but Free chunks size ({pool.Free}) is not equal Total pool size ({pool.Total})");
		}

		[Fact]
		public void WhenClearContent_ThenAllUnderlyingContentBytesShouldBeZeroed()
		{
			var r = new Random();
			var initialSize = r.Next(1000, 2000);
			var chunkSize = initialSize / 10;
			var pool = new LargeContentPool(initialSize, chunkSize, false);
			var content = pool.Acquire();
			var bytes = Enumerable.Range(0, chunkSize).Select(i => (byte)i).ToArray();
			var zeroBytes = Enumerable.Range(0, chunkSize).Select(_ => (byte)0).ToArray();
			content.ReadFrom(bytes, 0, bytes.Length);

			content.Clear();

			Assert.Equal(zeroBytes, content);
		}
	}
}
