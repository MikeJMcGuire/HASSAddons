using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HMX.HASSActron
{
	public static class AsyncExtension
	{
		public static async Task<bool> WaitOneAsync(this WaitHandle handle, int millisecondsTimeout, CancellationToken cancellationToken)
		{
			RegisteredWaitHandle registeredWaitHandle = null;
			CancellationTokenRegistration cancellationTokenRegistration = default;
			TaskCompletionSource<bool> taskCompletionSource;

			try
			{
				taskCompletionSource = new TaskCompletionSource<bool>();
				
				registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(handle, 
					(state, timedOut) => ((TaskCompletionSource<bool>) state).TrySetResult(!timedOut),
					taskCompletionSource, millisecondsTimeout, true);

				cancellationTokenRegistration = cancellationToken.Register(
					state => ((TaskCompletionSource<bool>)state).TrySetCanceled(),
					taskCompletionSource);

				return await taskCompletionSource.Task;
			}
			finally
			{
				if (registeredWaitHandle != null)
					registeredWaitHandle.Unregister(null);

				cancellationTokenRegistration.Dispose();
			}
		}
	}
}
