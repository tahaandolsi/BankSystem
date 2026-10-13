using BankSystem.Account.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BankSystem.Account.Infrastructure.Migrations;

[DbContext(typeof(AccountDbContext))]
[Migration("202610110001_AddAccountTransactionReceipts")]
public partial class AddAccountTransactionReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AccountTransactionReceipts",
            columns: table => new
            {
                TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AccountTransactionReceipts", x => x.TransactionId));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AccountTransactionReceipts");
    }
}
