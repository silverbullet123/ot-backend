using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Core.Entities
{
    public sealed record Player
    {
        public Guid AccountId { get; init; }
        public string Username { get; init; } = default!;
        public string? CountryCode { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
