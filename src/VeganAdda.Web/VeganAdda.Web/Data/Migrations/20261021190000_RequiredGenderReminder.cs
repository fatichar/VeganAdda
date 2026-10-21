using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeganAdda.Web.Data.Migrations;

[DbContext(typeof(CommunityDb))]
[Migration("20261021190000_RequiredGenderReminder")]
public class RequiredGenderReminder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO "Notifications" ("Id", "UserId", "Kind", "Text", "Url", "Read", "Created")
        SELECT gen_random_uuid(), p."Id", 'profile-gender-required',
            'Please update your profile: gender is now required.', '/settings/profile', FALSE, NOW()
        FROM "Profiles" p
        WHERE btrim(p."Gender") = '' AND NOT EXISTS (
            SELECT 1 FROM "Notifications" n
            WHERE n."UserId" = p."Id" AND n."Kind" = 'profile-gender-required');
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM "Notifications" WHERE "Kind" = 'profile-gender-required';
        """);
}
