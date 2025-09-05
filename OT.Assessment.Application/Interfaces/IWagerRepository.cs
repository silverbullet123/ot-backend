using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OT.Assessment.Application.Dtos;
using OT.Assessment.Application.Helpers;
using OT.Assessment.Core.Dtos;

namespace OT.Assessment.Application.Interfaces
{
    public interface IWagerRepository
    {
        Task InsertWagerAsync(CasinoWagerDto dto, CancellationToken ct = default);
        Task<PagedResult<WagerReadModel>> GetPlayerWagersAsync(Guid accountId, int page, int pageSize, CancellationToken ct = default);
        Task<IEnumerable<TopSpenderReadModel>> GetTopSpendersAsync(int count, CancellationToken ct = default);
    }
}
