using System;
using System.Net;
using System.Net.Sockets;

namespace OnYourMark
{
    /// <summary>
    /// 방 코드: Host의 IPv4 주소를 base64로 바꾼 짧은 문자열 (예: 192.168.0.12 → wKgADA).
    /// 화면에 IP를 그대로 보여 주지 않으려는 용도이며, 암호화가 아니므로 누구나 되돌릴 수 있다.
    /// IPv4 4바이트를 base64로 인코딩하고 끝의 '='는 뺀다. 대소문자를 구분한다.
    /// </summary>
    public static class RoomCode
    {
        public static string Encode(string ipv4)
        {
            if (!IPAddress.TryParse(ipv4, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return ipv4;
            return Convert.ToBase64String(ip.GetAddressBytes()).TrimEnd('=');
        }

        /// <summary>방 코드를 IP 주소로 바꾼다. IP를 그대로 넣어도 받아 준다. 잘못된 값이면 false.</summary>
        public static bool TryDecode(string input, out string address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string code = input.Trim();

            // IP 주소를 그대로 입력한 경우 (실행 인자 -join 등)
            if (code.Contains(".") && IPAddress.TryParse(code, out var direct)) { address = direct.ToString(); return true; }

            try
            {
                string padded = code + new string('=', (4 - code.Length % 4) % 4);
                var bytes = Convert.FromBase64String(padded);
                if (bytes.Length != 4) return false;
                address = new IPAddress(bytes).ToString();
                return true;
            }
            catch (FormatException) { return false; }
        }
    }
}
