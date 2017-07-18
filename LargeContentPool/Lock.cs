using System;
using System.Threading;

namespace LargeContentPool
{
	internal sealed class Lock : IDisposable
	{
		private SpinLock _sLock;
		private bool _lockTaken;

		public Lock()
		{
			_sLock = new SpinLock();
		}

		public IDisposable Enter()
		{
			_sLock.TryEnter(ref _lockTaken);

			return this;
		}

		public static Lock Create() => new Lock();

		public void Dispose()
		{
			if (_lockTaken)
			{
				_lockTaken = false;
				_sLock.Exit();
			}
		}
	}
}
