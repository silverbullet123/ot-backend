using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using OT.Assessment.Application.Interfaces;
using OT.Assessment.Core.Dtos;
using OT.Assessment.Infrastructure.Data;


namespace OT.Assessment.Infrastructure.Repositories
{
    public class FailedWagerRepository : IFailedWagerRepository
    {
        private readonly DapperConnectionFactory _factory;

        public FailedWagerRepository(DapperConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task InsertFailedWagerAsync(CasinoWagerDto dto, string rawMessage, CancellationToken ct = default)
        {
            await using var conn = _factory.CreateConnection();
            await conn.OpenAsync(ct);

            var sql = @"
                INSERT INTO FailedWagers (
                    WagerId, TransactionId, AccountId, BrandId, Username, CountryCode, GameName, Theme,
                    ProviderName, Amount, NumberOfBets, DurationMs, SessionData, RawMessage, CreatedDateTime
                ) VALUES (
                    @WagerId, @TransactionId, @AccountId, @BrandId, @Username, @CountryCode, @GameName, @Theme,
                    @ProviderName, @Amount, @NumberOfBets, @DurationMs, @SessionData, @RawMessage, @CreatedDateTime
                )";

            await conn.ExecuteAsync(sql, new
            {
                dto.WagerId,
                dto.TransactionId,
                dto.AccountId,
                dto.BrandId,
                dto.Username,
                dto.CountryCode,
                dto.GameName,
                dto.Theme,
                ProviderName = dto.Provider,
                Amount = Math.Round(dto.Amount, 2),
                dto.NumberOfBets,
                DurationMs = dto.Duration,
                dto.SessionData,
                RawMessage = rawMessage,
                CreatedDateTime = dto.CreatedDateTime.UtcDateTime
            });
        }
    }
}
