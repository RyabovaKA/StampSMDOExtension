using Ascon.Pilot.SDK;
using System;
using System.Threading.Tasks;

namespace StampSMDOExtension.Utilities
{
    public static class StampObjectLoader
    {
        public static Task<IDataObject> Load(IObjectsRepository repository, Guid id)
        {
            var tcs = new TaskCompletionSource<IDataObject>();
            IDisposable sub = null;

            sub = repository.SubscribeObjects(new[] { id }).Subscribe(
                obj =>
                {
                    if (obj.State != DataState.Loaded || obj.Id != id) return;
                    sub?.Dispose();
                    tcs.TrySetResult(obj);
                },
                ex => tcs.TrySetException(ex));

            return tcs.Task;
        }
    }
}