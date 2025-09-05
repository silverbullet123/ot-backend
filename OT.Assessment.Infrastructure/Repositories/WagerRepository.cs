using Dapper;
using Microsoft.Data.SqlClient;
using OT.Assessment.Application.Dtos;
using OT.Assessment.Application.Helpers;
using OT.Assessment.Application.Interfaces;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OT.Assessment.Infrastructure.Repositories
{
    public class WagerRepository : IWagerRepository
    {
        private readonly DapperConnectionFactory _factory;
        public WagerRepository(DapperConnectionFactory factory) => _factory = factory;

        public async Task InsertWagerAsync(CasinoWagerDto dto, CancellationToken ct = default)
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);

            // Call stored proc InsertWager (idempotent on TransactionId)
            await conn.ExecuteAsync(
                "dbo.InsertWager",
                new
                {
                    WagerId = dto.WagerId,
                    TransactionId = dto.TransactionId,
                    AccountId = dto.AccountId,
                    Username = dto.Username,
                    CountryCode = dto.CountryCode,
                    BrandId = dto.BrandId,
                    GameName = dto.GameName,
                    Theme = dto.Theme,
                    ProviderName = dto.Provider,
                    Amount = Math.Round(dto.Amount, 2),
                    NumberOfBets = dto.NumberOfBets,
                    DurationMs = dto.Duration,
                    SessionData = dto.SessionData,
                    CreatedDateTime = dto.CreatedDateTime.UtcDateTime
                },
                commandType: System.Data.CommandType.StoredProcedure);
        }

        public async Task<PagedResult<WagerReadModel>> GetPlayerWagersAsync(Guid accountId, int page, int pageSize, CancellationToken ct = default)
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);

            using var multi = await conn.QueryMultipleAsync("dbo.GetPlayerCasinoWagersPaged",
                new { AccountId = accountId, Page = page, PageSize = pageSize },
                commandType: System.Data.CommandType.StoredProcedure);

            var rows = (await multi.ReadAsync()).Select(r => new WagerReadModel(
                (Guid)r.WagerId,
                (string)r.Game,
                (string)r.Provider,
                (decimal)r.Amount,
                (DateTime)r.CreatedDateTime
            )).ToList();

            var total = await multi.ReadFirstAsync<int>();
            return new PagedResult<WagerReadModel>(rows, page, pageSize, total);
        }

        public async Task<IEnumerable<TopSpenderReadModel>> GetTopSpendersAsync(int count, CancellationToken ct = default)
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);

            var data = await conn.QueryAsync("dbo.GetTopSpenders", new { Count = count }, commandType: System.Data.CommandType.StoredProcedure);
            return data.Select(r => new TopSpenderReadModel((Guid)r.AccountId, (string)r.Username, (decimal)r.TotalAmountSpend));
        }
    }
}
