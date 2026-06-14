using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBot.Models;
public static class WarnStorage
{
    // Key = UserId, Value = List warnings
    public static Dictionary<ulong, List<string>> Warnings = new();
}
