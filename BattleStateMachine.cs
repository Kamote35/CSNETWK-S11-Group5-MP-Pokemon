using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleStateMachine : MonoBehaviour
{
    public enum BattleState { SETUP, WAITING_FOR_MOVE, PROCESSING_TURN, RESOLVING, GAME_OVER }
    public BattleState currentState;

    [Header("Network")]
    public NetworkManager networkManager;

    [Header("UI - Opponent")]
    public Image oppImage;
    public Slider oppHealthSlider;
    public TextMeshProUGUI oppNameText;

    [Header("UI - Player")]
    public Image playerImage;
    public Slider playerHealthSlider;
    public TextMeshProUGUI playerNameText;

    [Header("UI - Controls")]
    public Button[] attackButtons;
    public TextMeshProUGUI statusText;

    [Header("UI - Chat")]
    public TMP_InputField chatInput;
    public TextMeshProUGUI chatHistory;
    public Button sendChatButton;

    private bool isMyTurn;
    private string myPokemonName = "Pikachu"; 
    private string oppPokemonName = "Unknown";
    
    private int myMaxHP, myCurrentHP, myAtk, myDef;
    private int oppMaxHP, oppCurrentHP; 

    private string[] myMoves = new string[4];
    private int calculatedDamage = 0;
    private int seed;

    IEnumerator Start()
    {
        yield return null; 

        if (NetworkManager.Instance != null) {
            Debug.Log("Registering OnMessageReceived");        // <--- ADD
            NetworkManager.Instance.OnMessageReceived += HandleMessage;
        }
        else Debug.Log("NetworkManager instance missing!");

        for (int i = 0; i < attackButtons.Length; i++) {
            int index = i; 
            if(attackButtons[i] != null) 
                attackButtons[i].onClick.AddListener(() => OnAttackButton(index));
        }
        if (sendChatButton != null) sendChatButton.onClick.AddListener(OnSendChat);

        currentState = BattleState.SETUP;
        
        if (IsHost()) myPokemonName = "Charizard"; 
        else myPokemonName = "Blastoise";

        LoadMyStats();
        SetupMyMoves();
        
        if(playerNameText != null) playerNameText.text = myPokemonName;
        if(playerHealthSlider != null) {
            playerHealthSlider.maxValue = myMaxHP;
            playerHealthSlider.value = myCurrentHP;
        }

        if (NetworkManager.Instance.IsSpectator) {
            statusText.text = "Spectating...";
            ToggleButtons(false);
        }
        else {
            if (!IsHost()) {
                StartCoroutine(SpamBattleSetup());
            }
            else {
                statusText.text = "Waiting for Challenger...";
                ToggleButtons(false);
            }
        }
    }

    IEnumerator SpamBattleSetup()
    {
        while(currentState == BattleState.SETUP)
        {
            SendBattleSetup();
            yield return new WaitForSeconds(0.5f);
        }
    }

    void LoadMyStats()
    {
        if (PokemonDataManager.Instance != null && 
            PokemonDataManager.Instance.allPokemon.ContainsKey(myPokemonName))
        {
            var p = PokemonDataManager.Instance.allPokemon[myPokemonName];
            myMaxHP = p.hp;
            myCurrentHP = p.hp;
            myAtk = p.attack;
            myDef = p.defense;
        }
        else
        {
            myMaxHP = 100; myCurrentHP = 100; myAtk = 50; myDef = 50;
        }
    }

    void SetupMyMoves()
    {
        if (myPokemonName == "Charizard") {
            myMoves = new string[] { "Flamethrower", "Wing Attack", "Slash", "Fire Spin" };
        } else if (myPokemonName == "Blastoise") {
            myMoves = new string[] { "Hydro Pump", "Bite", "Rapid Spin", "Water Gun" };
        } else {
            myMoves = new string[] { "Tackle", "Quick Attack", "Growl", "Thunder Shock" };
        }

        if(attackButtons != null) {
            for(int i=0; i<4; i++) {
                if(attackButtons[i] != null)
                    attackButtons[i].GetComponentInChildren<TextMeshProUGUI>().text = myMoves[i];
            }
        }
    }

    void HandleMessage(PokeMessage msg)
    {
        Debug.Log("HandleMessage RECEIVED: " + msg.message_type);  // <-- ADD THIS
        switch (msg.message_type)
        {
            case "HANDSHAKE_RESPONSE":
                seed = int.Parse(msg.data["seed"]);
                Random.InitState(seed);
                break;

            case "BATTLE_SETUP":
                oppPokemonName = msg.data["pokemon_name"];
                if(oppNameText != null) oppNameText.text = oppPokemonName;
                
                if (IsHost())
                {
                    // === THIS IS THE MISSING LINE THAT FIXES IT ===
                    SendBattleSetup(); 
                    // ==============================================

                    isMyTurn = true;
                    statusText.text = "Your Turn!";
                    currentState = BattleState.WAITING_FOR_MOVE;
                    ToggleButtons(true);
                }
                else
                {
                    isMyTurn = false;
                    statusText.text = "Opponent's Turn...";
                    currentState = BattleState.WAITING_FOR_MOVE;
                    ToggleButtons(false);
                    StopAllCoroutines(); 
                }
                
                oppMaxHP = 100; 
                oppCurrentHP = 100; 
                if(oppHealthSlider != null) {
                    oppHealthSlider.maxValue = oppMaxHP;
                    oppHealthSlider.value = 100;
                }
                break;

            case "ATTACK_ANNOUNCE":
                string move = msg.data["move_name"];
                statusText.text = $"{oppPokemonName} used {move}!";
                NetworkManager.Instance.SendReliable("DEFENSE_ANNOUNCE");
                currentState = BattleState.PROCESSING_TURN;
                break;

            case "DEFENSE_ANNOUNCE":
                if (isMyTurn) PerformDamageCalculation();
                break;

            case "CALCULATION_REPORT":
                int theirCalc = int.Parse(msg.data["damage_dealt"]);
                NetworkManager.Instance.SendReliable("CALCULATION_CONFIRM");
                ApplyDamage(theirCalc, !isMyTurn); 
                EndTurn();
                break;

            case "CALCULATION_CONFIRM":
                ApplyDamage(calculatedDamage, false); 
                EndTurn();
                break;

            case "CHAT_MESSAGE":
                string sender = IsHost() ? "Opponent" : "Player"; 
                string text = msg.data["message_text"];
                if (msg.data["content_type"] == "STICKER") text = "[STICKER SENT]";
                if(chatHistory != null) chatHistory.text += $"\n{sender}: {text}";
                break;
        }
    }

    void OnAttackButton(int index)
    {
        if (!isMyTurn) return;
        string chosenMove = myMoves[index];
        var data = new Dictionary<string, string> { { "move_name", chosenMove } };
        NetworkManager.Instance.SendReliable("ATTACK_ANNOUNCE", data);
        statusText.text = $"You used {chosenMove}!";
        ToggleButtons(false); 
        currentState = BattleState.PROCESSING_TURN;
    }

    void PerformDamageCalculation()
    {
        float random = Random.Range(0.85f, 1.0f); 
        calculatedDamage = Mathf.FloorToInt((float)myAtk / 10f * 20f * random); 
        if (calculatedDamage < 1) calculatedDamage = 1;

        var data = new Dictionary<string, string>
        {
            { "attacker", myPokemonName },
            { "move_used", "Attack" },
            { "remaining_health", myCurrentHP.ToString() },
            { "damage_dealt", calculatedDamage.ToString() },
            { "defender_hp_remaining", (oppCurrentHP - calculatedDamage).ToString() },
            { "status_message", "Hit!" }
        };
        NetworkManager.Instance.SendReliable("CALCULATION_REPORT", data);
    }

    void ApplyDamage(int dmg, bool applyToMe)
    {
        if (applyToMe) {
            myCurrentHP -= dmg;
            if(playerHealthSlider != null) playerHealthSlider.value = myCurrentHP;
        } else {
            oppCurrentHP -= dmg;
            if(oppHealthSlider != null) oppHealthSlider.value = oppCurrentHP;
        }
    }

    void EndTurn()
    {
        isMyTurn = !isMyTurn;
        currentState = BattleState.WAITING_FOR_MOVE;
        if (isMyTurn) {
            statusText.text = "Your Turn!";
            ToggleButtons(true);
        } else {
            statusText.text = "Opponent's Turn...";
            ToggleButtons(false);
        }
    }

    void OnSendChat()
    {
        string text = chatInput.text;
        if (string.IsNullOrEmpty(text)) return;
        var data = new Dictionary<string, string> {
            { "sender_name", "Me" }, { "content_type", "TEXT" }, { "message_text", text }
        };
        NetworkManager.Instance.SendReliable("CHAT_MESSAGE", data);
        chatHistory.text += $"\nMe: {text}";
        chatInput.text = "";
    }

    bool IsHost() {
        return NetworkManager.Instance.isHost; 
    }

    void ToggleButtons(bool state)
    {
        if (attackButtons == null) return;
        foreach(var b in attackButtons) {
            if (b != null) b.interactable = state;
        }
    }

    public void SendBattleSetup()
    {
        var data = new Dictionary<string, string> {
            { "communication_mode", "P2P" },
            { "pokemon_name", myPokemonName },
            { "stat_boosts", "{}" } 
        };
        NetworkManager.Instance.SendReliable("BATTLE_SETUP", data);
    }
}