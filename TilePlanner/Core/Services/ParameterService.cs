using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace TilePlanner.Core.Services
{
    public static class ParameterService
    {
        public const string PARAM_H_GROUT = "Tile_HGroutGap";
        public const string PARAM_V_GROUT = "Tile_VGroutGap";
        public const string PARAM_WIDTH = "Tile_Width";
        public const string PARAM_HEIGHT = "Tile_Height";
        public const string PARAM_PATTERN = "Tile_PatternType"; 
        public const string PARAM_HOST_ID = "Tile_HostElementId";

        public static void InitializeSharedParameters(Document doc)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "TilePlannerSharedParams.txt");
            if (!File.Exists(tempPath))
            {
                using (StreamWriter sw = File.CreateText(tempPath))
                {
                    sw.WriteLine("# This is a Revit shared parameter file.");
                    sw.WriteLine("*META	VERSION	MINVERSION");
                    sw.WriteLine("META	2	1");
                    sw.WriteLine("*GROUP	ID	NAME");
                    sw.WriteLine("GROUP	1	磁磚計畫");
                    sw.WriteLine("*PARAM	GUID	NAME	DATATYPE	DATACATEGORY	GROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	{PARAM_H_GROUT}	LENGTH		1	1		1");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	{PARAM_V_GROUT}	LENGTH		1	1		1");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	{PARAM_WIDTH}	LENGTH		1	1		0");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	{PARAM_HEIGHT}	LENGTH		1	1		0");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	{PARAM_PATTERN}	INTEGER		1	1		0");
                    sw.WriteLine($"PARAM	{Guid.NewGuid()}	{PARAM_HOST_ID}	TEXT		1	1		0");
                }
            }

            string originalSharedParamFile = doc.Application.SharedParametersFilename;
            try
            {
                doc.Application.SharedParametersFilename = tempPath;
                DefinitionFile defFile = doc.Application.OpenSharedParameterFile();
                if (defFile == null) return;

                DefinitionGroup group = defFile.Groups.get_Item("磁磚計畫");
                if (group == null) return;

                Category partCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Parts);
                CategorySet catSet = doc.Application.Create.NewCategorySet();
                catSet.Insert(partCat);

                BindingMap bindingMap = doc.ParameterBindings;

                using (Transaction t = new Transaction(doc, "綁定磁磚參數"))
                {
                    t.Start();
                    foreach (Definition def in group.Definitions)
                    {
                        if (!BindingExists(doc, def.Name))
                        {
                            InstanceBinding instanceBinding = doc.Application.Create.NewInstanceBinding(catSet);
                            bindingMap.Insert(def, instanceBinding, GroupTypeId.Data);
                        }
                    }
                    t.Commit();
                }
            }
            finally
            {
                // Restore logic if needed
            }
        }

        private static bool BindingExists(Document doc, string paramName)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key.Name == paramName) return true;
            }
            return false;
        }

        public static void SetConfigParams(Element element, TileConfig config, string hostId)
        {
            element.LookupParameter(PARAM_H_GROUT)?.Set(config.HGroutGapFeet);
            element.LookupParameter(PARAM_V_GROUT)?.Set(config.VGroutGapFeet);
            element.LookupParameter(PARAM_WIDTH)?.Set(config.TileWidthFeet);
            element.LookupParameter(PARAM_HEIGHT)?.Set(config.TileHeightFeet);
            element.LookupParameter(PARAM_PATTERN)?.Set((int)config.PatternType);
            element.LookupParameter(PARAM_HOST_ID)?.Set(hostId);
        }

        public static TileConfig GetConfigFromElement(Element element)
        {
            var config = new TileConfig();
            
            double hGap = element.LookupParameter(PARAM_H_GROUT)?.AsDouble() ?? 0;
            double vGap = element.LookupParameter(PARAM_V_GROUT)?.AsDouble() ?? 0;
            double width = element.LookupParameter(PARAM_WIDTH)?.AsDouble() ?? 0;
            double height = element.LookupParameter(PARAM_HEIGHT)?.AsDouble() ?? 0;
            int pattern = element.LookupParameter(PARAM_PATTERN)?.AsInteger() ?? 0;

            if (width > 0) config.TileWidth = FeetToMm(width);
            if (height > 0) config.TileHeight = FeetToMm(height);
            config.HGroutGap = FeetToMm(hGap);
            config.VGroutGap = FeetToMm(vGap);
            config.PatternType = (TilePatternType)pattern;

            return config;
        }

        private static double FeetToMm(double feet) => feet * 304.8;
    }
}
