using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace LargeContentPool.Tests
{
	public sealed class LargeContentPoolTests
    {
		[Theory]
		[InlineData(100), InlineData(1000)]
	    public void GivenPool_WhenWriteSingleBytes_ThenAllBytesShouldBeWritesSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var buffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte) (x % byte.MaxValue)).ToArray();

			foreach (var b in bytes)
			{
				buffer.ReadFrom(b);
			}

			Assert.Equal(bytes, buffer);
		}

		[Theory]
		[InlineData(100), InlineData(1000)]
	    public void GivenPool_WhenWriteCollectionOfBytes_ThenAllBytesShouldBeWritesSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var buffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte) (x % byte.MaxValue)).ToArray();
			buffer.ReadFrom(bytes, 0, bytes.Length);

			Assert.Equal(bytes, buffer);
		}

		[Theory]
		[InlineData(100), InlineData(1000)]
	    public void GivenPool_WhenWriteDataFromStream_ThenAllBytesShouldBeWritesSuccessively(int byteCount)
		{
			var pool = new LargeContentPool(100, 10, false);
			var syncBuffer = pool.Acquire();
			var asyncBuffer = pool.Acquire();
			var bytes = Enumerable.Range(0, byteCount).Select(x => (byte) (x % byte.MaxValue)).ToArray();

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
				asyncBuffer.ReadFromAsync(memStream, CancellationToken.None).Wait();
			}

			Assert.Equal(bytes, syncBuffer);
			Assert.Equal(bytes, asyncBuffer);
		}
    }
}
