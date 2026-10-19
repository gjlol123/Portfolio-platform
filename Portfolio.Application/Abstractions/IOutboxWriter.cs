using System;
using System.Collections.Generic;
using System.Text;

namespace Portfolio.Application.Abstractions
{
    public interface IOutboxWriter
    {
        void Add<T>(
            string messageType,
            T payload,
            DateTimeOffset occurredAtUtc
            );

    }
}
