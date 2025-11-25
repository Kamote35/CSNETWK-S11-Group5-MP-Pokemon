import socket
import threading
import time
import csv
import random
import sys
import math

# --- CONFIGURATION ---
CSV_FILE = "pokemon.csv"
TIMEOUT = 0.5
MAX_RETRIES = 5

# --- MOVES DATABASE (Simplified) ---
MOVES = {
    "Flamethrower": {"type": "fire", "category": "Special", "power": 90},
    "Wing Attack":  {"type": "flying", "category": "Physical", "power": 60},
    "Slash":        {"type": "normal", "category": "Physical", "power": 70},
    "Fire Spin":    {"type": "fire", "category": "Special", "power": 35},
    "Hydro Pump":   {"type": "water", "category": "Special", "power": 110},
    "Bite":         {"type": "dark", "category": "Physical", "power": 60},
    "Rapid Spin":   {"type": "normal", "category": "Physical", "power": 50},
    "Water Gun":    {"type": "water", "category": "Special", "power": 40},
    "Tackle":       {"type": "normal", "category": "Physical", "power": 40},
}

# --- DATA LOADER ---
class PokemonData:
    def __init__(self):
        self.pokemon = {}
        self.load_data()

    def load_data(self):
        try:
            with open(CSV_FILE, 'r', encoding='utf-8') as f:
                reader = csv.reader(f)
                header = next(reader)
                
                # Map type columns
                types = ["bug", "dark", "dragon", "electric", "fairy", "fight", 
                         "fire", "flying", "ghost", "grass", "ground", "ice", 
                         "normal", "poison", "psychic", "rock", "steel", "water"]
                
                for row in reader:
                    if len(row) < 35: continue
                    
                    try:
                        name = row[30]
                        stats = {
                            "name": name,
                            "hp": int(row[28]),
                            "attack": int(row[19]),
                            "defense": int(row[25]),
                            "sp_attack": int(row[33]),
                            "sp_defense": int(row[34]),
                            "speed": int(row[35]),
                            "type1": row[36],
                            "type2": row[37] if len(row) > 37 else "",
                            "multipliers": {}
                        }
                        
                        # Load type effectiveness (Indices 1-18)
                        for i, t in enumerate(types):
                            if row[i+1]:
                                stats["multipliers"][t] = float(row[i+1])
                        
                        self.pokemon[name] = stats
                    except:
                        continue
            print(f"[System] Loaded {len(self.pokemon)} Pokemon.")
        except FileNotFoundError:
            print("[Error] pokemon.csv not found! Please create it.")
            sys.exit()

    def get_pokemon(self, name):
        return self.pokemon.get(name, self.pokemon["Bulbasaur"]) # Default fallback

# --- PROTOCOL MESSAGE HELPER ---
class PokeMessage:
    def __init__(self, msg_type, seq=0, data=None):
        self.msg_type = msg_type
        self.seq = seq
        self.data = data if data else {}

    def serialize(self):
        lines = [f"message_type: {self.msg_type}"]
        if self.msg_type == "ACK":
            lines.append(f"ack_number: {self.seq}")
        else:
            lines.append(f"sequence_number: {self.seq}")
            for k, v in self.data.items():
                lines.append(f"{k}: {v}")
        return "\n".join(lines)

    @staticmethod
    def deserialize(text):
        lines = text.strip().split('\n')
        data = {}
        msg_type = ""
        seq = 0
        ack = 0
        
        for line in lines:
            if ":" in line:
                key, val = line.split(":", 1)
                key = key.strip()
                val = val.strip()
                if key == "message_type": msg_type = val
                elif key == "sequence_number": seq = int(val)
                elif key == "ack_number": ack = int(val)
                else: data[key] = val
        
        msg = PokeMessage(msg_type, seq, data)
        if msg_type == "ACK": msg.seq = ack 
        return msg

# --- NETWORKING & LOGIC ---
class BattleNode:
    def __init__(self, port, peer_ip, peer_port, is_host):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(('0.0.0.0', port))
        self.peer_addr = (peer_ip, peer_port)
        self.is_host = is_host
        
        self.seq_out = 1
        self.seq_in_needed = 1
        self.running = True
        self.message_queue = []
        self.acked_seqs = set()
        
        self.db = PokemonData()
        
        # Game State
        self.my_pokemon = None
        self.opp_pokemon = None
        self.my_hp = 0
        self.opp_hp = 0
        self.my_moves = []
        self.seed = 0
        self.state = "SETUP"

    def start(self):
        threading.Thread(target=self.listen_loop, daemon=True).start()
        self.setup_game()
        self.game_loop()

    def send_reliable(self, msg_type, data=None):
        msg = PokeMessage(msg_type, self.seq_out, data)
        payload = msg.serialize().encode('utf-8')
        
        for _ in range(MAX_RETRIES):
            self.sock.sendto(payload, self.peer_addr)
            start_time = time.time()
            
            # Wait for ACK
            while time.time() - start_time < TIMEOUT:
                if self.seq_out in self.acked_seqs:
                    self.seq_out += 1
                    return
                time.sleep(0.05)
            print(f"[Net] Retrying {msg_type}...")
        
        print("[Error] Connection Lost.")
        sys.exit()

    def send_ack(self, ack_num):
        msg = PokeMessage("ACK", ack_num)
        self.sock.sendto(msg.serialize().encode('utf-8'), self.peer_addr)

    def listen_loop(self):
        while self.running:
            try:
                data, addr = self.sock.recvfrom(4096)
                # Update peer addr if dynamic (especially for Host)
                if self.is_host and self.peer_addr[0] == '127.0.0.1' and addr[0] != '127.0.0.1':
                     # Only needed for complex setups, keeping simple for localhost
                     pass

                text = data.decode('utf-8')
                msg = PokeMessage.deserialize(text)

                if msg.msg_type == "ACK":
                    self.acked_seqs.add(msg.seq)
                    continue

                # Send ACK immediately
                self.send_ack(msg.seq)

                # Process if new
                self.message_queue.append(msg)
                
            except Exception as e:
                print(f"Error receiving: {e}")

    def wait_for_message(self, expected_type=None):
        while True:
            if self.message_queue:
                msg = self.message_queue.pop(0)
                if expected_type and msg.msg_type != expected_type:
                    # Store for later or handle specific out of order
                    continue 
                return msg
            time.sleep(0.1)

    # --- GAME LOGIC ---

    def setup_game(self):
        if self.is_host:
            print("Waiting for Joiner...")
            # Wait for Handshake
            msg = self.wait_for_message("HANDSHAKE_REQUEST")
            print("Joiner connected!")
            
            # Send Response with Seed
            self.seed = random.randint(1, 10000)
            self.send_reliable("HANDSHAKE_RESPONSE", {"seed": str(self.seed)})
            
            self.my_pokemon = self.db.get_pokemon("Charizard")
            self.my_moves = ["Flamethrower", "Wing Attack", "Slash", "Fire Spin"]
        else:
            print("Connecting to Host...")
            # Send Handshake
            self.send_reliable("HANDSHAKE_REQUEST")
            
            # Wait for Response
            msg = self.wait_for_message("HANDSHAKE_RESPONSE")
            self.seed = int(msg.data["seed"])
            print(f"Connected! Seed synced: {self.seed}")
            
            self.my_pokemon = self.db.get_pokemon("Blastoise")
            self.my_moves = ["Hydro Pump", "Bite", "Rapid Spin", "Water Gun"]

        random.seed(self.seed)
        self.my_hp = self.my_pokemon["hp"]
        
        # Battle Setup Exchange
        print(f"I chose {self.my_pokemon['name']}")
        self.send_reliable("BATTLE_SETUP", {
            "pokemon_name": self.my_pokemon["name"],
            "communication_mode": "P2P",
            "stat_boosts": "{}"
        })
        
        msg = self.wait_for_message("BATTLE_SETUP")
        self.opp_pokemon = self.db.get_pokemon(msg.data["pokemon_name"])
        self.opp_hp = self.opp_pokemon["hp"]
        print(f"Opponent chose {self.opp_pokemon['name']}")
        
        self.state = "WAITING_FOR_MOVE"

    def game_loop(self):
        turn_counter = 0
        while self.state != "GAME_OVER":
            print(f"\n--- TURN {turn_counter + 1} ---")
            print(f"My HP: {self.my_hp} | Opp HP: {self.opp_hp}")
            
            is_my_turn = (self.is_host and turn_counter % 2 == 0) or (not self.is_host and turn_counter % 2 != 0)
            
            if is_my_turn:
                # My Turn
                print("Your move! Choose 0-3:")
                for i, m in enumerate(self.my_moves): print(f"{i}: {m}")
                
                while True:
                    try:
                        choice = int(input("> "))
                        if 0 <= choice <= 3: break
                    except: pass
                
                move_name = self.my_moves[choice]
                print(f"You used {move_name}!")
                self.send_reliable("ATTACK_ANNOUNCE", {"move_name": move_name})
                
                # Wait for them to be ready
                self.wait_for_message("DEFENSE_ANNOUNCE")
                
                # Calculate Damage
                dmg, eff = self.calculate_damage(move_name, self.my_pokemon, self.opp_pokemon)
                print(f"Dealt {dmg} damage! ({eff})")
                
                # Report
                self.opp_hp -= dmg
                self.send_reliable("CALCULATION_REPORT", {
                    "attacker": self.my_pokemon["name"],
                    "move_used": move_name,
                    "damage_dealt": str(dmg),
                    "defender_hp_remaining": str(self.opp_hp),
                    "status_message": eff
                })
                
                self.wait_for_message("CALCULATION_CONFIRM")
                
            else:
                # Opponent Turn
                print("Waiting for opponent...")
                msg = self.wait_for_message("ATTACK_ANNOUNCE")
                move_name = msg.data["move_name"]
                print(f"Opponent used {move_name}!")
                
                self.send_reliable("DEFENSE_ANNOUNCE")
                
                # Calculate Damage (Verification)
                dmg, eff = self.calculate_damage(move_name, self.opp_pokemon, self.my_pokemon)
                
                msg = self.wait_for_message("CALCULATION_REPORT")
                reported_dmg = int(msg.data["damage_dealt"])
                
                if abs(reported_dmg - dmg) > 1:
                    print(f"[Warning] Discrepancy! Calc: {dmg}, Rept: {reported_dmg}")
                
                self.my_hp -= reported_dmg
                print(f"Took {reported_dmg} damage! ({eff})")
                self.send_reliable("CALCULATION_CONFIRM")

            if self.my_hp <= 0:
                print("You Fainted! You Lose.")
                self.send_reliable("GAME_OVER", {"winner": self.opp_pokemon["name"]})
                self.state = "GAME_OVER"
            elif self.opp_hp <= 0:
                # Wait for them to tell us they lost
                msg = self.wait_for_message("GAME_OVER")
                print("Opponent Fainted! You Win!")
                self.state = "GAME_OVER"
            
            turn_counter += 1

    def calculate_damage(self, move_name, attacker, defender):
        move = MOVES.get(move_name, {"type": "normal", "category": "Physical", "power": 40})
        
        # 1. Select Stats
        if move["category"] == "Physical":
            atk = attacker["attack"]
            defense = defender["defense"]
        else:
            atk = attacker["sp_attack"]
            defense = defender["sp_defense"]
            
        # 2. Type Effectiveness
        # Check defender's type multipliers for the move's type
        # Our CSV structure: defender["multipliers"][attacking_type]
        move_type = move["type"]
        multiplier = defender["multipliers"].get(move_type, 1.0)
        
        # 3. Formula (Simplified from PDF)
        # (Power * Atk * Multiplier) / Def
        raw = (move["power"] * atk * multiplier) / defense
        dmg = max(1, int(raw))
        
        eff_text = "It's effective."
        if multiplier > 1: eff_text = "Super effective!"
        if multiplier < 1: eff_text = "Not very effective..."
        
        return dmg, eff_text

# --- MAIN ENTRY POINT ---
if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage:")
        print("  Run Host:   python battle.py host <my_port>")
        print("  Run Joiner: python battle.py join <host_ip> <host_port> <my_port>")
        sys.exit()

    mode = sys.argv[1]
    
    if mode == "host":
        my_port = int(sys.argv[2])
        # Initially, we don't know peer port, Joiner will msg us first
        game = BattleNode(my_port, "127.0.0.1", 0, True) 
        print(f"Hosting on port {my_port}...")
        
        # Wait for first packet to set peer address
        data, addr = game.sock.recvfrom(1024)
        game.peer_addr = addr
        game.message_queue.append(PokeMessage.deserialize(data.decode('utf-8'))) # Re-queue the handshake
        game.start()
        
    elif mode == "join":
        host_ip = sys.argv[2]
        host_port = int(sys.argv[3])
        my_port = int(sys.argv[4])
        
        game = BattleNode(my_port, host_ip, host_port, False)
        game.start()