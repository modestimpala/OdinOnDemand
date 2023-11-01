using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;

namespace OdinOnDemand.Utils
{
    // Data Transfer Object for PieceConfig so we can serialize it to JSON. Otherwise, we serialize too much unneeded data and things break.
    [Serializable]
    public class PieceConfigDTO
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string PieceTable { get; set; }
        public RequirementConfig[] Requirements { get; set; }
        
        public static PieceConfigDTO ToDTO(PieceConfig config)
        {
            return new PieceConfigDTO
            {
                Name = config.Name,
                Category = config.Category,
                PieceTable = config.PieceTable,
                Requirements = config.Requirements
            };
        }
        
        public static List<PieceConfigDTO> ToDTOList(List<PieceConfig> configs)
        {
            return configs.Select(ToDTO).ToList();
        }
        
    }

}