using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thor.DataLayer.Migrations.Master
{
    /// <inheritdoc />
    public partial class TenantRoutingUniqueUserPoolId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Lambda authorizer resolves a Cognito token's tenant from its user pool, so two
            // tenants sharing a pool would be ambiguous. Stop with a clear message naming the
            // shared pools instead of failing on the bare unique-index violation.
            migrationBuilder.Sql("""
                DO $$
                DECLARE duplicate_pools text;
                BEGIN
                    SELECT string_agg(user_pool_id, ', ') INTO duplicate_pools
                    FROM (SELECT user_pool_id FROM auth.tenant_routing GROUP BY user_pool_id HAVING count(*) > 1) d;

                    IF duplicate_pools IS NOT NULL THEN
                        RAISE EXCEPTION 'auth.tenant_routing has user_pool_id values shared by more than one tenant (%); give each tenant its own pool before applying this migration.', duplicate_pools;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_routing_user_pool_id",
                schema: "auth",
                table: "tenant_routing",
                column: "user_pool_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenant_routing_user_pool_id",
                schema: "auth",
                table: "tenant_routing");
        }
    }
}
