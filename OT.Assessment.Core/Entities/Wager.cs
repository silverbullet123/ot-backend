using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Core.Entities
{
    public sealed record Wager
    {
        public Guid WagerId { get; init; }
        public Guid TransactionId { get; init; }
        public Guid AccountId { get; init; }
        public Guid BrandId { get; init; }
        public int GameId { get; init; }
        public decimal Amount { get; init; }
        public int NumberOfBets { get; init; }
        public long? DurationMs { get; init; }
        public string? SessionData { get; init; }
        public DateTimeOffset CreatedDateTime { get; init; }
    }
}
