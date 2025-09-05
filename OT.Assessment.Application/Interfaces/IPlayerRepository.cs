using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Application.Interfaces
{
    public interface IPlayerRepository
    {
        Task UpsertPlayerAsync(Guid accountId, string username, string? countryCode, CancellationToken ct = default);
    }
}
