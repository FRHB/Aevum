using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoAevumStudio.Infrastructure.Migrations
{
    public partial class AddAdminsView : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE VIEW [dbo].[Admins] AS
                SELECT 
                    StudioId AS AdminID,
                    FirstName AS AdminFirstName,
                    LastName AS AdminLastName,
                    MiddleName AS AdminMiddleName,
                    PasswordHash AS AdminPassword
                FROM AspNetUsers
                WHERE StudioId LIKE 'SADM-%' OR StudioId LIKE 'ADM-%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [dbo].[Admins];");
        }
    }
}
