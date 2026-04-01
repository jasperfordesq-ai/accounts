using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accounts.Api.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AccountsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Database=accounts;Username=accounts;Password=accounts_dev");
        return new AccountsDbContext(optionsBuilder.Options);
    }
}
