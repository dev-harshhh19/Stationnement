using Npgsql;

Console.WriteLine("🇮🇳 Indian Cities Parking Migration");
Console.WriteLine("===================================\n");

// Load connection string from environment or .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2) Environment.SetEnvironmentVariable(parts[0], parts[1]);
    }
}

var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
var dbName = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser = Environment.GetEnvironmentVariable("DB_USERNAME");
var dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");

if (string.IsNullOrEmpty(dbHost) || string.IsNullOrEmpty(dbPass))
{
    Console.WriteLine("❌ Error: Database credentials not found!");
    Console.WriteLine("   Please set environment variables or create .env file with DB_HOST, DB_NAME, DB_USERNAME, DB_PASSWORD");
    return;
}

var cs = $"Host={dbHost};Database={dbName};Username={dbUser};Password={dbPass};SslMode=Require";

var cities = new List<(string city, string state, double lat, double lng, int slots, decimal price)>
{
    // Metro Cities
    ("Mumbai", "Maharashtra", 19.0760, 72.8777, 100, 50),
    ("Delhi", "Delhi NCR", 28.6139, 77.2090, 100, 45),
    ("Bangalore", "Karnataka", 12.9716, 77.5946, 100, 40),
    ("Hyderabad", "Telangana", 17.3850, 78.4867, 80, 35),
    ("Chennai", "Tamil Nadu", 13.0827, 80.2707, 80, 35),
    ("Kolkata", "West Bengal", 22.5726, 88.3639, 80, 30),
    ("Pune", "Maharashtra", 18.5204, 73.8567, 70, 35),
    ("Ahmedabad", "Gujarat", 23.0225, 72.5714, 60, 30),
    
    // Tier 1 Cities
    ("Jaipur", "Rajasthan", 26.9124, 75.7873, 50, 25),
    ("Lucknow", "Uttar Pradesh", 26.8467, 80.9462, 50, 25),
    ("Chandigarh", "Punjab", 30.7333, 76.7794, 50, 30),
    ("Surat", "Gujarat", 21.1702, 72.8311, 50, 25),
    ("Kochi", "Kerala", 9.9312, 76.2673, 50, 30),
    ("Nagpur", "Maharashtra", 21.1458, 79.0882, 50, 25),
    ("Indore", "Madhya Pradesh", 22.7196, 75.8577, 50, 25),
    ("Bhopal", "Madhya Pradesh", 23.2599, 77.4126, 50, 25),
    ("Visakhapatnam", "Andhra Pradesh", 17.6868, 83.2185, 50, 25),
    ("Patna", "Bihar", 25.5941, 85.1376, 50, 20),
    ("Vadodara", "Gujarat", 22.3072, 73.1812, 40, 25),
    ("Coimbatore", "Tamil Nadu", 11.0168, 76.9558, 50, 25),
    
    // Tier 2 Cities
    ("Thiruvananthapuram", "Kerala", 8.5241, 76.9366, 40, 25),
    ("Gurgaon", "Haryana", 28.4595, 77.0266, 60, 40),
    ("Noida", "Uttar Pradesh", 28.5355, 77.3910, 60, 35),
    ("Mysore", "Karnataka", 12.2958, 76.6394, 40, 25),
    ("Mangalore", "Karnataka", 12.9141, 74.8560, 40, 25),
    ("Udaipur", "Rajasthan", 24.5854, 73.7125, 40, 30),
    ("Jodhpur", "Rajasthan", 26.2389, 73.0243, 40, 25),
    ("Agra", "Uttar Pradesh", 27.1767, 78.0081, 50, 30),
    ("Varanasi", "Uttar Pradesh", 25.3176, 82.9739, 50, 25),
    ("Amritsar", "Punjab", 31.6340, 74.8723, 40, 25),
    ("Ranchi", "Jharkhand", 23.3441, 85.3096, 40, 20),
    ("Raipur", "Chhattisgarh", 21.2514, 81.6296, 40, 20),
    ("Guwahati", "Assam", 26.1445, 91.7362, 40, 25),
    ("Bhubaneswar", "Odisha", 20.2961, 85.8245, 40, 25),
    ("Dehradun", "Uttarakhand", 30.3165, 78.0322, 40, 25),
    ("Vijayawada", "Andhra Pradesh", 16.5062, 80.6480, 40, 20),
    ("Madurai", "Tamil Nadu", 9.9252, 78.1198, 40, 20),
    ("Nashik", "Maharashtra", 19.9975, 73.7898, 40, 25),
    ("Rajkot", "Gujarat", 22.3039, 70.8022, 40, 20),
    ("Faridabad", "Haryana", 28.4089, 77.3178, 40, 30),
    ("Ghaziabad", "Uttar Pradesh", 28.6692, 77.4538, 40, 30),
    ("Jabalpur", "Madhya Pradesh", 23.1815, 79.9864, 40, 20),
    ("Aurangabad", "Maharashtra", 19.8762, 75.3433, 40, 20),
    ("Shimla", "Himachal Pradesh", 31.1048, 77.1734, 30, 30),
    ("Goa", "Goa", 15.2993, 74.1240, 50, 35),
    ("Pondicherry", "Puducherry", 11.9416, 79.8083, 30, 25),
    ("Jamshedpur", "Jharkhand", 22.8046, 86.2029, 40, 20),
    ("Dharamshala", "Himachal Pradesh", 32.2190, 76.3234, 30, 25),
    ("Tirupati", "Andhra Pradesh", 13.6288, 79.4192, 40, 25),
    ("Kanpur", "Uttar Pradesh", 26.4499, 80.3319, 50, 20),
    ("Navi Mumbai", "Maharashtra", 19.0330, 73.0297, 60, 35),
    ("Thane", "Maharashtra", 19.2183, 72.9781, 50, 30),
};

Console.WriteLine("🔗 Connecting to database...");
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();
Console.WriteLine("✓ Connected\n");

// Add missing columns to parking_locations
Console.WriteLine("📋 Ensuring all columns exist...");
var alterCmds = new[] {
    "ALTER TABLE parking_locations ADD COLUMN IF NOT EXISTS latitude DECIMAL(10,6)",
    "ALTER TABLE parking_locations ADD COLUMN IF NOT EXISTS longitude DECIMAL(10,6)",
    "ALTER TABLE parking_slots ADD COLUMN IF NOT EXISTS is_premium BOOLEAN DEFAULT FALSE",
    "ALTER TABLE parking_slots ADD COLUMN IF NOT EXISTS floor INTEGER DEFAULT 1",
    "ALTER TABLE parking_slots ADD COLUMN IF NOT EXISTS section VARCHAR(10) DEFAULT 'A'",
    "ALTER TABLE parking_slots ADD COLUMN IF NOT EXISTS is_active BOOLEAN DEFAULT TRUE"
};
foreach (var sql in alterCmds) {
    try { await new NpgsqlCommand(sql, conn).ExecuteNonQueryAsync(); }
    catch { /* Column might already exist */ }
}
Console.WriteLine("✓ Schema updated\n");

// Ensure user_subscriptions table exists
await new NpgsqlCommand(@"
    CREATE TABLE IF NOT EXISTS user_subscriptions (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        user_id UUID REFERENCES users(id) ON DELETE CASCADE,
        tier VARCHAR(20) NOT NULL DEFAULT 'free',
        starts_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
        expires_at TIMESTAMP WITH TIME ZONE,
        is_active BOOLEAN DEFAULT TRUE,
        created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
        UNIQUE(user_id)
    )", conn).ExecuteNonQueryAsync();

// Clear existing data for fresh start
Console.WriteLine("🗑️ Clearing existing locations...");
await new NpgsqlCommand("DELETE FROM reservations; DELETE FROM parking_slots; DELETE FROM parking_locations;", conn).ExecuteNonQueryAsync();

int cityCount = 0;
int slotCount = 0;

Console.WriteLine("\n🏙️ Adding Indian cities:\n");

foreach (var (city, state, lat, lng, slots, price) in cities)
{
    var locationId = Guid.NewGuid();
    var locationName = $"{city} Central Parking";
    var address = $"Main Road, {city}";
    
    await using var cmd = new NpgsqlCommand(@"
        INSERT INTO parking_locations (id, name, address, city, latitude, longitude, total_slots, base_price_per_hour, is_active, created_at)
        VALUES (@id, @name, @address, @city, @lat, @lng, @slots, @price, TRUE, NOW())", conn);
    
    cmd.Parameters.AddWithValue("id", locationId);
    cmd.Parameters.AddWithValue("name", locationName);
    cmd.Parameters.AddWithValue("address", address);
    cmd.Parameters.AddWithValue("city", city);
    cmd.Parameters.AddWithValue("lat", lat);
    cmd.Parameters.AddWithValue("lng", lng);
    cmd.Parameters.AddWithValue("slots", slots);
    cmd.Parameters.AddWithValue("price", price);
    
    await cmd.ExecuteNonQueryAsync();
    cityCount++;
    
    // Create slots - 20% premium slots
    int premiumCount = (int)(slots * 0.2);
    
    for (int i = 1; i <= slots; i++)
    {
        var slotId = Guid.NewGuid();
        var floor = (i - 1) / 20 + 1;
        var slotNum = ((i - 1) % 20) + 1;
        var code = $"F{floor}-{(char)('A' + (slotNum - 1) / 10)}{slotNum % 10:D2}";
        var isPremium = i <= premiumCount;
        
        await using var slotCmd = new NpgsqlCommand(@"
            INSERT INTO parking_slots (id, location_id, slot_code, slot_type, floor, status, is_premium, base_price_per_hour, created_at)
            VALUES (@id, @locationId, @code, @type, @floor, 'available', @premium, @price, NOW())", conn);
        
        slotCmd.Parameters.AddWithValue("id", slotId);
        slotCmd.Parameters.AddWithValue("locationId", locationId);
        slotCmd.Parameters.AddWithValue("code", code);
        slotCmd.Parameters.AddWithValue("type", isPremium ? "Premium" : "Standard");
        slotCmd.Parameters.AddWithValue("floor", floor);
        slotCmd.Parameters.AddWithValue("premium", isPremium);
        slotCmd.Parameters.AddWithValue("price", isPremium ? price * 1.5m : price);
        
        await slotCmd.ExecuteNonQueryAsync();
        slotCount++;
    }
    
    Console.WriteLine($"  ✓ {city} ({state}) - {slots} slots, ₹{price}/hr");
}

Console.WriteLine($"\n" + new string('=', 50));
Console.WriteLine($"✅ MIGRATION COMPLETE!");
Console.WriteLine($"   🏙️ Cities added: {cityCount}");
Console.WriteLine($"   🅿️ Total slots: {slotCount}");
Console.WriteLine($"   ⭐ Premium slots: {slotCount / 5} (20%)");
Console.WriteLine(new string('=', 50));
