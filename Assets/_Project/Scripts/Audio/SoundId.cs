namespace OnYourMark
{
    /// <summary>
    /// 효과음 종류. 번호는 Assets/AudioEffect/AudioEffect_manifest.csv의 csv_row(이펙트 · 사운드 목록 번호)와 같다.
    /// 2번은 점프 / 착지 두 소리라 Jump, Landing으로 나뉜다.
    /// </summary>
    public enum SoundId
    {
        Footsteps = 1, Jump = 2, Landing = 102, JetpackThrust = 3, DashIgnite = 4, InfiniteStep = 5, OutOfBoundsReturn = 6,
        SwordSwing = 7, SwordHit = 8, ParryStart = 9, ParrySuccess = 10, NanoBoost = 11, NanoBoostMini = 12,
        RapidFire = 13, RailgunFire = 14, DryFire = 15, OverchargeFire = 16, WeaponTransform = 17, ZoomFocus = 18,
        SmartLockOn = 19, AwakenedRailgunLaser = 20,
        HealShield = 21, ShieldHold = 22, RemoteReload = 23, HealthAbsorb = 24, DashChargeReload = 25,
        EnemySpawn = 26, RusherMove = 27, RusherCountdown = 28, RusherExplosion = 29, LaserCharge = 30, EnemyLaserFire = 31,
        EnemyHit = 32, EnemyDeath = 33,
        PlayerHit = 34, DownedWarning = 35, ReviveProgress = 36, ReviveComplete = 37,
        BattleMusic = 38, WaveAlert = 39, TerminalUnlock = 40, HudSwap = 41, TradeOffer = 42, UltimateReady = 43,
        RadioChirp = 44, ResultJingle = 45, UiClick = 46,
    }
}
