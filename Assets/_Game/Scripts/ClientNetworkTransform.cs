using Unity.Netcode.Components;

// Pozycję ustala właściciel obiektu, a nie serwer
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}