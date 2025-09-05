using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Core.Entities
{
    public sealed record Game
    {
        public int GameId { get; init; }
        public string GameName { get; init; } = default!;
        public string? Theme { get; init; }
        public int ProviderId { get; init; }
    }
}
