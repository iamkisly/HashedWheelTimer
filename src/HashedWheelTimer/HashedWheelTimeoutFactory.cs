using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    // Using pooling for such small objects results in decreased performance.
    // Should only be used when the number of allocations is a critical factor.
    /*
    private class HashedWheelTimeoutFactory(IPooledObjectPolicy<HashedWheelTimeout> policy)
        : ITimeoutFactory<HashedWheelTimeout>
    {
        private readonly ObjectPool<HashedWheelTimeout> _pool = new DefaultObjectPool<HashedWheelTimeout>(policy, 256);

        public HashedWheelTimeout Create() => _pool.Get();
        public void Return(HashedWheelTimeout timeout) => _pool.Return(timeout);
    }
    */

    public class PooledTimeoutPolicy : IPooledObjectPolicy<ITimeout>
    {
        public ITimeout Create() => throw new NotImplementedException();

        public bool Return(ITimeout obj) => throw new NotImplementedException();
        
    }
}
