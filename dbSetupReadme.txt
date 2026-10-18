This file is instructions on setting up the local databse used for our project.

Step 1
Download & Install PostgeSQL
https://www.postgresql.org/

Step 2 - execute query
CREATE DATABASE StoreSimDB;

step 3 - init tables
-- Drop existing tables if starting again
DROP TABLE IF EXISTS cart_items CASCADE;
DROP TABLE IF EXISTS customers CASCADE;
DROP TABLE IF EXISTS products CASCADE;
DROP TABLE IF EXISTS binary_assets CASCADE;

-- Create products table
CREATE TABLE products (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100),
    description TEXT,
    category VARCHAR(50),
    quantity INT,
    price DECIMAL(18, 2)
);

-- Create customers table
CREATE TABLE customers (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);

-- Create cart_items table with unique constraint for UPSERT logic
CREATE TABLE cart_items (
    id SERIAL PRIMARY KEY,
    customer_id INT REFERENCES customers(id) ON DELETE CASCADE,
    product_id INT REFERENCES products(id) ON DELETE CASCADE,
    quantity INT NOT NULL,
    CONSTRAINT unique_customer_product UNIQUE (customer_id, product_id)
);

-- Store logo asset storage using binary data to avoid OS-level file permission issues
CREATE TABLE binary_assets (
    entity_id INT,
    entity_type VARCHAR(50),
    image_data BYTEA,
    PRIMARY KEY (entity_id, entity_type)
);

step 4 - populate tables
INSERT INTO products (name, description, category, quantity, price)
SELECT 
    CASE (i % 6)
        WHEN 0 THEN (ARRAY['Mystic', 'Epic', 'Legendary', 'Collector''s', 'Standard', 'Dark'])[floor(random() * 6 + 1)::int] || ' ' || 
                     (ARRAY['Booster Pack', 'Starter Deck', 'Collector Box', 'Foil Promo Card', 'Deck Box', 'Playmat'])[floor(random() * 6 + 1)::int]
        WHEN 1 THEN (ARRAY['Deluxe', 'Co-op', 'Strategic', 'Expansion', 'Standalone', 'Fast-Paced'])[floor(random() * 6 + 1)::int] || ' ' || 
                     (ARRAY['Board Game', 'Card Game', 'Dungeon Crawler', 'Strategy Game', 'Party Game', 'Campaign Game'])[floor(random() * 6 + 1)::int]
        WHEN 2 THEN (ARRAY['Metal', 'Gemstone', 'Polyhedral 7-Die', 'Oversized D20', 'Silicone', 'Glowing'])[floor(random() * 6 + 1)::int] || ' ' || 
                     (ARRAY['Dice Set', 'Dice Bag', 'Dice Tray', 'Spin-Down Counter', 'Dice Tower', 'Precision Dice'])[floor(random() * 6 + 1)::int]
        WHEN 3 THEN (ARRAY['Painted', 'Unpainted', 'Resin', 'Detailed', 'Monster', 'Heroic'])[floor(random() * 6 + 1)::int] || ' ' || 
                     (ARRAY['Dragon Miniature', 'Warrior Mini', 'Orc Horde', 'Wizard Figurine', 'Terrain Ruin', 'Mecha Model'])[floor(random() * 6 + 1)::int]
        WHEN 4 THEN (ARRAY['Core', 'Advanced', 'Campaign', 'Supplement', 'World', 'Rule'])[floor(random() * 6 + 1)::int] || ' ' || 
                     (ARRAY['Rulebook', 'Monster Manual', 'Sourcebook', 'Adventure Module', 'Campaign Guide', 'Player Handbook'])[floor(random() * 6 + 1)::int]
        ELSE (ARRAY['Acrylic', 'Fine Detail', 'Matte', 'Sharp', 'Pro', 'Hobby'])[floor(random() * 6 + 1)::int] || ' ' || 
             (ARRAY['Paint Set', 'Brush Kit', 'Clippers', 'Glue Bottle', 'Cutting Mat', 'Primer Spray'])[floor(random() * 6 + 1)::int]
    END AS name,
    
    'High-quality tabletop hobby item, perfect for game nights and collectors. Stock ref #' || i AS description,
    
    CASE (i % 6)
        WHEN 0 THEN 'Trading Cards'
        WHEN 1 THEN 'Board Games'
        WHEN 2 THEN 'Dice'
        WHEN 3 THEN 'Miniatures'
        WHEN 4 THEN 'RPG Books'
        ELSE 'Hobby Supplies'
    END AS category,
    
    CAST(floor(random() * 90 + 10) AS INT),
    ROUND(CAST(random() * 145 + 5 AS NUMERIC), 2)
FROM generate_series(1, 1000) AS i;

step 5 - insert store ascii art
INSERT INTO binary_assets (entity_id, entity_type, image_data)
VALUES (1, 'StoreLogoASCII', convert_to(E'      /\\      \n     /  \\     \n    / /\\ \\    \n   | [20] |   \n    \\ \\/ /    \n     \\  /     \n      \\/      \nTABLETOP HAVEN', 'UTF8'))
ON CONFLICT (entity_id, entity_type) 
DO UPDATE SET image_data = EXCLUDED.image_data;

step 6 - change password in StoreSim.slnx in StoreSim project under store.cs to match your database password (ctrl+f InsertPasswordHere)
namespace StoreSim
{
    public class Store
    {
        // Explicit toggles for failure states
        public static bool SimulateConnectionDrop { get; set; } = false;
        public static int SimulatedLatencyMs { get; set; } = 0;

        private readonly string _connectionString = "Host=localhost;Port=5432;Database=StoreSimDB;Username=postgres;Password=InsertPasswordHere";
