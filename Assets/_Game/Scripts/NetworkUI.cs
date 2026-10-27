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
            string status = nm.IsHost ? "Host" : "Klient";
            GUI.Label(new Rect(10, Screen.height - 30, 300, 25), status);
            return;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();

        GUI.Label(new Rect(10, 10, 200, 25), "Adres IP hosta:");
        ip = GUI.TextField(new Rect(10, 35, 200, 25), ip);

        if (GUI.Button(new Rect(10, 70, 200, 35), "Hostuj grę"))
        {
            // 0.0.0.0 = przyjmuj połączenia z sieci, nie tylko z tego komputera
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            nm.StartHost();
        }

        if (GUI.Button(new Rect(10, 110, 200, 35), "Dołącz"))
        {
            transport.SetConnectionData(ip, Port);
            nm.StartClient();
        }
    }
}