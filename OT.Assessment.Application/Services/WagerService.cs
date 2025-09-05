using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OT.Assessment.Application.Interfaces;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Application.Dtos;
using OT.Assessment.Application.Helpers;

namespace OT.Assessment.Application.Services
{
    public class WagerService
    {
        private readonly IWagerRepository _wagerRepo;

        public WagerService(IWagerRepository wagerRepo)
        {
            _wagerRepo = wagerRepo;
        }

        public async Task InsertWagerAsync(CasinoWagerDto dto, CancellationToken ct = default)
        {
            // Basic business validations
            if (dto.Amount < 0) throw new ArgumentException("Amount must be >= 0", nameof(dto.Amount));
            if (dto.WagerId == Guid.Empty || dto.AccountId == Guid.Empty || dto.BrandId == Guid.Empty || dto.TransactionId == Guid.Empty)
                throw new ArgumentException("GUIDs must be provided");

            await _wagerRepo.InsertWagerAsync(dto, ct);
        }

        public Task<PagedResult<WagerReadModel>> GetPlayerWagersAsync(Guid accountId, int page, int pageSize, CancellationToken ct = default)
            => _wagerRepo.GetPlayerWagersAsync(accountId, page, pageSize, ct);

        public Task<IEnumerable<TopSpenderReadModel>> GetTopSpendersAsync(int count, CancellationToken ct = default)
            => _wagerRepo.GetTopSpendersAsync(count, ct);
    }
}
