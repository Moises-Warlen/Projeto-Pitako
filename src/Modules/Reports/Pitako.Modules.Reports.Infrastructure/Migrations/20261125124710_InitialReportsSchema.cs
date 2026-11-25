using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pitako.Modules.Reports.Infrastructure.Persistence;
using Pitako.Persistence;

#nullable disable

namespace Pitako.Modules.Reports.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReportsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reports");

            migrationBuilder.CreateTable(
                name: "report_export_audit_logs",
                schema: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_export_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_report_export_audit_logs_tenant_id_category_exported_at_utc",
                schema: "reports",
                table: "report_export_audit_logs",
                columns: new[] { "tenant_id", "category", "exported_at_utc" });

            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildEnableTenantRlsSql(ReportsDbContext.SchemaName, "report_export_audit_logs"));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(PostgresRlsSqlGenerator.BuildDisableTenantRlsSql(ReportsDbContext.SchemaName, "report_export_audit_logs"));
            }
            migrationBuilder.DropTable(
                name: "report_export_audit_logs",
                schema: "reports");
        }
    }
}
