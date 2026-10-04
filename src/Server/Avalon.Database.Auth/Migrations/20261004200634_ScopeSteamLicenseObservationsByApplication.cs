using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avalon.Database.Auth.Migrations
{
    /// <inheritdoc />
    public partial class ScopeSteamLicenseObservationsByApplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LicenseObservations_AccountId_Provider_ProviderSubject_Envi~",
                table: "LicenseObservations");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseObservations_AccountId_Provider_ProviderSubject_Envi~",
                table: "LicenseObservations",
                columns: new[] { "AccountId", "Provider", "ProviderSubject", "Environment", "Product", "ProviderAppId", "ObservedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LicenseObservations_AccountId_Provider_ProviderSubject_Envi~",
                table: "LicenseObservations");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseObservations_AccountId_Provider_ProviderSubject_Envi~",
                table: "LicenseObservations",
                columns: new[] { "AccountId", "Provider", "ProviderSubject", "Environment", "Product", "ObservedAt" });
        }
    }
}
