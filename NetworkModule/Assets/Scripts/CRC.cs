
namespace Assets.Scripts
{
    //crc32 체크
    public class CRC32
    {
        readonly static uint POLYNOMIAL = 0xEDB88320;

        static uint[] crcTable = new uint[256];   //crc테이블

        static bool bCreateTable = false;


        //crc테이블 생성
        static void GenerateCrcTable()
        {
            uint crcAccum;

            for (uint i = 0; i < 256; i++)
            {
                crcAccum = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crcAccum & 1) > 0)
                    {
                        crcAccum = (crcAccum >> 1) ^ POLYNOMIAL;
                    }
                    else
                    {
                        crcAccum = (crcAccum >> 1);
                    }
                }
                crcTable[i] = crcAccum;
            }
        }

        public static uint GetCRC(string input, uint crc = 0)
        {
            //테이블 한번만 생성
            if (bCreateTable == false)
            {
                GenerateCrcTable();
                bCreateTable = true;
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input);

            return calcCRC(bytes, bytes.Length, crc);
        }

        public static uint GetCRC(byte[] bytes, int aLength, uint crc = 0)
        {
            if (bCreateTable == false)
            {
                GenerateCrcTable();
                bCreateTable = true;
            }

            return calcCRC(bytes, aLength, crc);
        }

        static uint calcCRC(byte[] bytes, int iSize, uint crc)
        {
            crc = ~crc;

            for (int i = 0; i < iSize; i++)
            {
                crc = crcTable[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }
    }
}
