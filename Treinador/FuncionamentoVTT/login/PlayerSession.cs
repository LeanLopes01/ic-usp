using UnityEngine;

public class PlayerSession : MonoBehaviour
{
    public static PlayerSession Instance;

    public int playerId;
    public string playerName;
    public string email;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
