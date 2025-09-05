using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace OT.Assessment.Core.Dtos
{
    public record CasinoWagerDto
    {
        [Required] public Guid WagerId { get; init; }
        public string? Theme { get; init; }
        [Required] public string Provider { get; init; } = default!;
        [Required] public string GameName { get; init; } = default!;
        [Required] public Guid TransactionId { get; init; }
        [Required] public Guid BrandId { get; init; }
        [Required] public Guid AccountId { get; init; }
        [Required] public string Username { get; init; } = default!;
        public string? ExternalReferenceId { get; init; }
        public Guid? TransactionTypeId { get; init; }
        [Range(0, double.MaxValue)] public decimal Amount { get; init; }
        [Required] public DateTimeOffset CreatedDateTime { get; init; }
        [Range(1, int.MaxValue)] public int NumberOfBets { get; init; }
        public string? CountryCode { get; init; }
        public string? SessionData { get; init; }
        public long? Duration { get; init; }
    }
}
