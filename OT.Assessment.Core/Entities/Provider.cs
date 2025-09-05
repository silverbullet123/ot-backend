using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Core.Entities
{
    public sealed record Provider
    {
        public int ProviderId { get; init; }
        public string ProviderName { get; init; } = default!;
    }
}
