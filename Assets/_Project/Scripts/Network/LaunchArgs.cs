using System;

namespace OnYourMark
{
    /// <summary>
    /// 실행 인자. 빌드를 바로 접속시켜 테스트할 때 쓴다.
    ///   -host            : 바로 Host 시작
    ///   -join 10.0.0.5   : 그 주소로 바로 Join
    ///   -role 0 | 1      : 포지션 팝업이 뜨면 자동 선택 (0 = P1 Sword, 1 = P2 Railgun)
    /// </summary>
    public static class LaunchArgs
    {
        public static bool Has(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        public static string Get(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
