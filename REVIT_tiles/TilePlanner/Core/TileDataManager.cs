using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace TilePlanner.Core
{
    /// <summary>
    /// 管理磁磚計畫資料儲存至 Revit Element 的 Extensible Storage
    /// </summary>
    public static class TileDataManager
    {
        // 定義此外掛專屬的 Schema ID (請使用獨一無二的 Guid，因前次錯誤已更新避免衝突)
        public static readonly Guid SCHEMA_GUID = new Guid("C7D8E9F0-1122-3344-5566-778899AABBCC");

        /// <summary>
        /// 取得或建立 Schema
        /// </summary>
        public static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SCHEMA_GUID);
            if (schema != null)
                return schema;

            SchemaBuilder builder = new SchemaBuilder(SCHEMA_GUID);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public); // 為了簡化，設定為Public
            builder.SetSchemaName("TilePlannerConfig");
            builder.SetDocumentation("Stores configuration for TilePlanner auto-updating");

            // 定義欄位 (對應 TileConfig 屬性)，Revit 2024 強制要求 double 必須指定單位 (SpecTypeId)
            builder.AddSimpleField("TileWidth", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("TileHeight", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("TileThickness", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("GroutWidth", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("GroutThickness", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("PatternType", typeof(int)); // enum 轉 int
            builder.AddSimpleField("RunningBondOffset", typeof(double)).SetSpec(SpecTypeId.Number);
            
            // 記錄主體牆的 ID (以便追蹤)
            builder.AddSimpleField("HostElementId", typeof(ElementId));

            return builder.Finish();
        }

        /// <summary>
        /// 將 TileConfig 儲存到 Element (通常是生成的帷幕牆/磁磚層) 中
        /// </summary>
        public static void SaveTileConfig(Element element, TileConfig config, ElementId hostId)
        {
            Schema schema = GetSchema();
            Entity entity = new Entity(schema);

            entity.Set("TileWidth", config.TileWidth, UnitTypeId.Feet);
            entity.Set("TileHeight", config.TileHeight, UnitTypeId.Feet);
            entity.Set("TileThickness", config.TileThickness, UnitTypeId.Feet);
            entity.Set("GroutWidth", config.GroutWidth, UnitTypeId.Feet);
            entity.Set("GroutThickness", config.GroutThickness, UnitTypeId.Feet);
            entity.Set("PatternType", (int)config.PatternType);
            entity.Set("RunningBondOffset", config.RunningBondOffset, UnitTypeId.General);
            entity.Set("HostElementId", hostId);

            element.SetEntity(entity);
        }

        /// <summary>
        /// 從 Element 讀取 TileConfig
        /// </summary>
        public static TileConfig LoadTileConfig(Element element, out ElementId hostId)
        {
            hostId = ElementId.InvalidElementId;
            Schema schema = GetSchema();

            if (schema == null) return null;

            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid()) return null; // 沒有資料

            TileConfig config = new TileConfig
            {
                TileWidth = entity.Get<double>("TileWidth", UnitTypeId.Feet),
                TileHeight = entity.Get<double>("TileHeight", UnitTypeId.Feet),
                TileThickness = entity.Get<double>("TileThickness", UnitTypeId.Feet),
                GroutWidth = entity.Get<double>("GroutWidth", UnitTypeId.Feet),
                GroutThickness = entity.Get<double>("GroutThickness", UnitTypeId.Feet),
                PatternType = (TilePatternType)entity.Get<int>("PatternType"),
                RunningBondOffset = entity.Get<double>("RunningBondOffset", UnitTypeId.General)
            };

            hostId = entity.Get<ElementId>("HostElementId");
            return config;
        }

        /// <summary>
        /// 檢查該 Element 是否包含 TileConfig 資料
        /// </summary>
        public static bool HasTileConfig(Element element)
        {
            Schema schema = GetSchema();
            if (schema == null) return false;

            Entity entity = element.GetEntity(schema);
            return entity.IsValid();
        }

        /// <summary>
        /// 刪除該 Element 上的 TileConfig 資料
        /// </summary>
        public static void RemoveTileConfig(Element element)
        {
            Schema schema = GetSchema();
            if (schema != null && element.GetEntity(schema).IsValid())
            {
                element.DeleteEntity(schema);
            }
        }
    }
}
