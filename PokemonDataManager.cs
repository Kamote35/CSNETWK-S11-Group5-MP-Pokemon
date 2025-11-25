using System.Collections.Generic;
using UnityEngine;

public class PokemonDataManager : MonoBehaviour
{
    public static PokemonDataManager Instance;

    public class PokemonStats
    {
        public string name;
        public int hp;
        public int attack;
        public int defense;
        public int sp_attack;
        public int sp_defense;
        public int speed;
        public string type1;
        public string type2;
        public Dictionary<string, float> typeMultipliers; 
    }

    public Dictionary<string, PokemonStats> allPokemon = new Dictionary<string, PokemonStats>();

    void Awake()
    {
        // Singleton pattern to ensure only one Manager exists
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Keep this alive between scenes
            LoadPokemonData();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LoadPokemonData()
    {
        // Load the file named "pokemon" from the Resources folder
        TextAsset csvFile = Resources.Load<TextAsset>("pokemon"); 
        
        if (csvFile == null)
        {
            Debug.LogError("CRITICAL ERROR: pokemon.csv not found in Resources folder!");
            return;
        }

        string[] lines = csvFile.text.Split('\n');
        
        // Loop through lines, skipping the header (index 0)
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] data = lines[i].Split(',');
            
            // Basic safety check for column count
            if (data.Length < 35) continue; 

            try 
            {
                PokemonStats p = new PokemonStats();
                p.typeMultipliers = new Dictionary<string, float>();

                // 1. Parse Type Effectiveness (Columns 1 to 18)
                string[] types = { "bug", "dark", "dragon", "electric", "fairy", "fight", "fire", "flying", "ghost", 
                                   "grass", "ground", "ice", "normal", "poison", "psychic", "rock", "steel", "water" };
                
                for(int t=0; t<types.Length; t++)
                {
                    // "against_bug" is at index 1
                    if (float.TryParse(data[t+1], out float multiplier))
                        p.typeMultipliers[types[t]] = multiplier;
                }

                // 2. Parse Stats (Indices based on your CSV structure)
                p.attack = int.Parse(data[19]);
                p.defense = int.Parse(data[25]);
                p.hp = int.Parse(data[28]);
                p.name = data[30];
                p.sp_attack = int.Parse(data[33]);
                p.sp_defense = int.Parse(data[34]);
                p.speed = int.Parse(data[35]);
                p.type1 = data[36];
                
                // Handle Type 2 (might be empty)
                p.type2 = (data.Length > 37) ? data[37] : "";

                if (!allPokemon.ContainsKey(p.name))
                {
                    allPokemon.Add(p.name, p);
                }
            }
            catch (System.Exception e)
            {
                // Just log warnings for bad rows, don't crash
                Debug.LogWarning($"Skipping row {i}: {e.Message}");
            }
        }
        Debug.Log($"Success! Loaded {allPokemon.Count} Pokemon from CSV.");
    }
}