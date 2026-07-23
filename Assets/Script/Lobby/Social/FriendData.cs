[System.Serializable]
public class FriendData
{
    public string Nickname;
    public int Level;
    public string InDate; // 친구 요청 및 관리에 필요한 inDate 추가

    public FriendData(string nickname, int level, string inDate)
    {
        Nickname = nickname;
        Level = level;
        InDate = inDate;
    }
}
