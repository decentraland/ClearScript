// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

extern "C"
{
    unsigned __int128 __udivti3(unsigned __int128 n, unsigned __int128 d)
    {
        unsigned __int128 q = 0;
        int shift = 0;

        while ((d << 1) && (d << 1) <= n)
        {
            d <<= 1;
            shift++;
        }

        for (; shift >= 0; --shift)
        {
            q <<= 1;
            if (n >= d)
            {
                n -= d;
                q |= 1;
            }

            d >>= 1;
        }

        return q;
    }

    unsigned __int128 __umodti3(unsigned __int128 n, unsigned __int128 d)
    {
        int shift = 0;

        while ((d << 1) && (d << 1) <= n)
        {
            d <<= 1;
            shift++;
        }

        for (; shift >= 0; --shift)
        {
            if (n >= d) n -= d;
            d >>= 1;
        }

        return n;
    }
}
