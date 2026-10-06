using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class SharedCommerce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderPriceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CheckoutEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    SuccessUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CancelUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CheckoutReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstDispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplayDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.CheckConstraint("CK_PaymentAttempt_Version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "PaymentDisputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentDisputes", x => x.Id);
                    table.CheckConstraint("CK_PaymentDispute_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentDisputes_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedBy = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Unresolved = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstDispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplayDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.CheckConstraint("CK_PaymentRefund_Version", "\"Version\" > 0");
                    table.CheckConstraint("CK_PaymentRefunds_Amount", "\"AmountMinor\" > 0 AND length(trim(\"Reason\")) BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Accounts_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    OriginalPurchaserAccountId = table.Column<long>(type: "bigint", nullable: false),
                    Product = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentEnvironment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LicenseEnvironment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Unresolved = table.Column<bool>(type: "boolean", nullable: false),
                    FundingAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaxMinor = table.Column<long>(type: "bigint", nullable: true),
                    SubtotalMinor = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FulfilledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconciliationIssue = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrder_Version", "\"Version\" > 0");
                    table.CheckConstraint("CK_PurchaseOrders_Amount", "\"AmountMinor\" > 0 AND length(\"Currency\") = 3 AND \"Currency\" = lower(\"Currency\")");
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Accounts_OriginalPurchaserAccountId",
                        column: x => x.OriginalPurchaserAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_GameLicenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "GameLicenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_PaymentAttempts_FundingAttemptId",
                        column: x => x.FundingAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Environment = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.CheckConstraint("CK_PaymentEvent_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PurchaseOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId",
                table: "PaymentAttempts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Provider_ProviderAccountId_Environment_Chec~",
                table: "PaymentAttempts",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "CheckoutReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Provider_ProviderAccountId_Environment_Oper~",
                table: "PaymentAttempts",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Provider_ProviderAccountId_Environment_Paym~",
                table: "PaymentAttempts",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "PaymentReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDisputes_PaymentAttemptId",
                table: "PaymentDisputes",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDisputes_Provider_ProviderAccountId_Environment_Exte~",
                table: "PaymentDisputes",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_OrderId",
                table: "PaymentEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentAttemptId",
                table: "PaymentEvents",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_Provider_ProviderAccountId_Environment_Extern~",
                table: "PaymentEvents",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_State_NextAttemptAt",
                table: "PaymentEvents",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PaymentAttemptId",
                table: "PaymentRefunds",
                column: "PaymentAttemptId",
                unique: true,
                filter: "\"Unresolved\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_Provider_ProviderAccountId_Environment_Exter~",
                table: "PaymentRefunds",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_Provider_ProviderAccountId_Environment_Opera~",
                table: "PaymentRefunds",
                columns: new[] { "Provider", "ProviderAccountId", "Environment", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_RequestedBy",
                table: "PaymentRefunds",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_AccountId_Product_LicenseEnvironment",
                table: "PurchaseOrders",
                columns: new[] { "AccountId", "Product", "LicenseEnvironment" },
                unique: true,
                filter: "\"Unresolved\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_FundingAttemptId",
                table: "PurchaseOrders",
                column: "FundingAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_LicenseId",
                table: "PurchaseOrders",
                column: "LicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OriginalPurchaserAccountId",
                table: "PurchaseOrders",
                column: "OriginalPurchaserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAttempts_PurchaseOrders_OrderId",
                table: "PaymentAttempts",
                column: "OrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAttempts_PurchaseOrders_OrderId",
                table: "PaymentAttempts");

            migrationBuilder.DropTable(
                name: "PaymentDisputes");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "PaymentAttempts");
        }
    }
}
