using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetworkUI : MonoBehaviour
{
    private string ip = "127.0.0.1";
    private const ushort Port = 7777;

    void OnGUI()
    {
        NetworkManager nm = NetworkManager.Singleton;

        if (nm.IsClient || nm.IsServer)
        {
            string status = nm.IsHost ? "Host  (F5 - zapis)" : "Klient";
            GUI.Label(new Rect(10, Screen.height - 30, 300, 25), status);
            return;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();

        if (GUI.Button(new Rect(10, 10, 200, 35), "Nowa gra"))
        {
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            if (nm.StartHost())
                GameState.Instance.ServerSetWorld(Random.Range(1, int.MaxValue), new Vector3(0f, 1f, 0f));
        }

        bool hasSave = SaveManager.Instance != null && SaveManager.Instance.HasSave;
        GUI.enabled = hasSave;
        if (GUI.Button(new Rect(10, 50, 200, 35), hasSave ? "Wczytaj grę" : "Brak zapisu"))
        {
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            if (nm.StartHost()) SaveManager.Instance.LoadGame();
        }
        GUI.enabled = true;

        GUI.Label(new Rect(10, 100, 200, 25), "Adres IP hosta:");
        ip = GUI.TextField(new Rect(10, 125, 200, 25), ip);

        if (GUI.Button(new Rect(10, 160, 200, 35), "Dołącz"))
        {
            transport.SetConnectionData(ip, Port);
            nm.StartClient();
        }
    }
}