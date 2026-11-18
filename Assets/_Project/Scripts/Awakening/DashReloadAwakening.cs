namespace OnYourMark
{
    /// <summary>2번 파츠 반환 각성 (P2): Shift 대쉬를 쓰면 탄창이 즉시 가득 찬다.</summary>
    [UnityEngine.CreateAssetMenu(menuName = "On Your Mark/Awakening/Dash Reload")]
    public class DashReloadAwakening : Awakening
    {
        public override void OnDash(AwakeningSet owner)
        {
            owner.Me.Ammo.Fill();
            SoundRelay.All(SoundId.DashChargeReload, owner.Me.transform.position + UnityEngine.Vector3.up);
        }
    }
}
