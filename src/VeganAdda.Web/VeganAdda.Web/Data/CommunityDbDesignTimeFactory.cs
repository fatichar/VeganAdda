using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VeganAdda.Web.Data;

public sealed class CommunityDbDesignTimeFactory : IDesignTimeDbContextFactory<CommunityDb>
{
    public CommunityDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CommunityDb>()
            .UseNpgsql("Host=localhost;Database=veganadda-design-time;Username=postgres")
            .Options;
        return new CommunityDb(options);
    }
}
