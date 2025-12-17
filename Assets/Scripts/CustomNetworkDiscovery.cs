using System;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

public struct DiscoveryResponse : NetworkMessage
{
    public long serverId;
    public Uri uri;
    public string hostName;
    public int currentPlayerCount;
    public int maxPlayerCount;
}

public struct DiscoveryRequest : NetworkMessage
{
}

public class CustomNetworkDiscovery : NetworkDiscoveryBase<DiscoveryRequest, DiscoveryResponse>
{
    public static System.Random RandomSource = new System.Random();
    [Tooltip("PlayerPrefs key for host nickname")]
    public string prefsKey = "PlayerNickname";
    private long myServerId;

    #region Server

    public override void Start()
    {
        base.Start();
        myServerId = RandomSource.Next();
    }

    protected override DiscoveryResponse ProcessRequest(DiscoveryRequest request, IPEndPoint endpoint)
    {
        var nm = NetworkManager.singleton as NetworkManagerRawr;
        int currentPlayers = nm != null ? nm.numPlayers : 0;
        int maxPlayers = nm != null ? nm.maxConnections : 4;
        string hName = PlayerPrefs.GetString(prefsKey, "Unknown Host");

        return new DiscoveryResponse
        {
            serverId = myServerId, 
            
            uri = transport.ServerUri(),
            hostName = hName,
            currentPlayerCount = currentPlayers,
            maxPlayerCount = maxPlayers
        };
    }

    #endregion

    #region Client

    protected override DiscoveryRequest GetRequest()
    {
        return new DiscoveryRequest();
    }

    protected override void ProcessResponse(DiscoveryResponse response, IPEndPoint endpoint)
    {
        response.uri = RealUri(response.uri, endpoint);
        OnServerFound.Invoke(response);
    }

    private Uri RealUri(Uri uri, IPEndPoint endpoint)
    {
        UriBuilder builder = new UriBuilder(uri);
        builder.Host = endpoint.Address.ToString();
        return builder.Uri;
    }

    #endregion
}