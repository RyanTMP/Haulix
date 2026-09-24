namespace Haulix.Core.Profiles;

/// <summary>
/// Display names for common ETS2 cargo ids. Save files store ids truncated to 12 characters
/// (e.g. <c>used_packag</c>); HAULIX also learns exact names from live telemetry.
/// </summary>
public static class CargoNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["used_packag"] = "Used Packaging", ["large_cont"] = "Large Containers", ["ibc_cont"] = "IBC Containers",
        ["med_vaccine"] = "Medical Vaccines", ["plast_film"] = "Plastic Film", ["roofing_felt"] = "Roofing Felt",
        ["metal_beams"] = "Metal Beams", ["lumber"] = "Lumber", ["logs"] = "Logs", ["hipresstank"] = "High Pressure Tank",
        ["largetubes"] = "Large Tubes", ["canned_food"] = "Canned Food", ["machine_part"] = "Machine Parts",
        ["frozen_food"] = "Frozen Food", ["fresh_veget"] = "Fresh Vegetables", ["fresh_fish"] = "Fresh Fish",
        ["beverages"] = "Beverages", ["beverages_c"] = "Canned Beverages", ["furniture"] = "Furniture",
        ["electronics"] = "Electronics", ["tires"] = "Tyres", ["cars"] = "Cars", ["cement"] = "Cement",
        ["concr_beams"] = "Concrete Beams", ["concr_stair"] = "Concrete Stairs", ["bricks"] = "Bricks",
        ["gravel"] = "Gravel", ["sand"] = "Sand", ["coal"] = "Coal", ["wheat"] = "Wheat", ["corn"] = "Corn",
        ["potatoes"] = "Potatoes", ["apples"] = "Apples", ["milk"] = "Milk", ["cheese"] = "Cheese", ["butter"] = "Butter",
        ["yogurt"] = "Yogurt", ["meat"] = "Meat", ["pork_meat"] = "Pork Meat", ["chicken_meat"] = "Chicken Meat",
        ["beef_meat"] = "Beef Meat", ["sausages"] = "Sausages", ["eggs"] = "Eggs", ["flour"] = "Flour", ["sugar"] = "Sugar",
        ["salt"] = "Salt", ["ice_cream"] = "Ice Cream", ["chocolate"] = "Chocolate", ["pasta"] = "Pasta", ["rice"] = "Rice",
        ["diesel"] = "Diesel", ["gasoline"] = "Petrol", ["fuel_oil"] = "Fuel Oil", ["lpg"] = "LPG", ["acid"] = "Acid",
        ["chemicals"] = "Chemicals", ["explosives"] = "Explosives", ["fireworks"] = "Fireworks", ["magnesium"] = "Magnesium",
        ["nitrogen"] = "Nitrogen", ["chlorine"] = "Chlorine", ["hydrogen"] = "Hydrogen", ["ammunition"] = "Ammunition",
        ["pesticides"] = "Pesticides", ["fertilizer"] = "Fertiliser", ["paper"] = "Paper", ["cardboard"] = "Cardboard",
        ["wood_bark"] = "Wood Bark", ["sawpanels"] = "Sawn Panels", ["pipes"] = "Pipes", ["steel_coil"] = "Steel Coils",
        ["aluminium"] = "Aluminium", ["copper"] = "Copper", ["scrap_metal"] = "Scrap Metal", ["glass"] = "Glass",
        ["windows"] = "Windows", ["doors"] = "Doors", ["cables"] = "Cables", ["transformer"] = "Transformer",
        ["excavator"] = "Excavator", ["tractors"] = "Tractors", ["forklifts"] = "Forklifts", ["boat"] = "Boat",
        ["yacht"] = "Yacht", ["train_part"] = "Train Parts", ["wind_blade"] = "Wind Turbine Blade",
        ["cattle"] = "Cattle", ["pigs"] = "Pigs", ["sheep"] = "Sheep", ["chickens"] = "Chickens",
        ["clothes"] = "Clothes", ["shoes"] = "Shoes", ["toys"] = "Toys", ["books"] = "Books", ["medicine"] = "Medicine",
        ["pharmaceut"] = "Pharmaceuticals", ["cosmetics"] = "Cosmetics", ["office_sup"] = "Office Supplies",
        ["household"] = "Household Goods", ["appliances"] = "Appliances", ["washing_mach"] = "Washing Machines",
        ["air_cond"] = "Air Conditioners", ["computers"] = "Computers", ["tv"] = "Televisions", ["hay"] = "Hay",
        ["straw"] = "Straw Bales", ["wood_chips"] = "Wood Chips", ["plywood"] = "Plywood", ["mdf"] = "MDF Boards",
        ["asphalt_mix"] = "Asphalt Mix", ["contaminated"] = "Contaminated Material", ["waste"] = "Waste",
        ["empty_barr"] = "Empty Barrels", ["empty_palet"] = "Empty Pallets", ["pallets"] = "Pallets",
    };

    public static string? Lookup(string? id) => id is not null && Map.TryGetValue(id, out var n) ? n : null;
}
