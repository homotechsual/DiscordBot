using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DiscordBot.Core.Data;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// </summary>
public class HomotechsualBotContextFactory : IDesignTimeDbContextFactory<HomotechsualBotContext>
{
    public HomotechsualBotContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HomotechsualBotContext>();
        optionsBuilder.UseSqlite("Data Source=./homotechsualbot.db");
        return new HomotechsualBotContext(optionsBuilder.Options);
    }
}
