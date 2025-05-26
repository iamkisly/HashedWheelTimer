using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashedWheelTimer
{
    public interface ITimeoutFactory<TTimeout> where TTimeout : class, ITimeout
    {
        TTimeout Create();
        void Return(TTimeout timeout);
    }
}
