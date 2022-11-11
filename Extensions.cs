using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThunderRoad;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Extensions
{
    public static class LevelExtensions
    {
        public static bool ParseLevelOption(this Level level, string name, bool defaultValue)
        {
            if (level.options != null && level.options.TryGetValue(name, out string val))
            {
                if (val == "0")
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }
    }
}
