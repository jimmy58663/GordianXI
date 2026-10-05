// src/Gordian.Core/Events/EventVm.Sound.cs
namespace Gordian.Core.Events
{
    public sealed partial class EventVm
    {
        /// <summary>
        /// The music and sound volume opcodes (#167). Layouts referenced from XiEvents (https://github.com/atom0s/XiEvents),
        /// <c>OpCodes/0x005C</c>, <c>0x005D</c>, <c>0x0069</c>, <c>0x006A</c> and <c>0x009A</c>:
        /// <list type="bullet">
        /// <item>0x5C subs 0-7 (4 bytes): music slot <c>sub</c> = work(2). Subs 0x80-0x87 (6 bytes): slot <c>sub &amp; 7</c> =
        /// work(2), with a start volume work(4) (not applied: GordianXI starts every track at full volume). Subs 0xA0 / 0xA1
        /// (6 bytes): ease the music to work(2) over work(4). Another sub: retail returns without advancing; the request
        /// ends.</item>
        /// <item>0x5D (5 bytes): ease the music to work(1) over work(3).</item>
        /// <item>0x69 (4 bytes): the categories in mask work(2) go silent when byte 1 is not 0, else full.</item>
        /// <item>0x6A (7 bytes): the categories in mask work(5) ease to work(1) / 1000 over work(3).</item>
        /// <item>0x9A (1 byte): yields until the music has the slot's track loaded.</item>
        /// </list>
        /// </summary>
        private void ExecSound(byte op)
        {
            switch (op)
            {
                case 0x5C:
                {
                    int sub = Code8(1);
                    if (sub <= 7)
                    {
                        _host.SetMusicSlot(sub, GetWork(2));
                        _pc += 4;
                    }
                    else if (sub is >= 0x80 and <= 0x87)
                    {
                        _host.SetMusicSlot(sub & 7, GetWork(2));
                        _pc += 6;
                    }
                    else if (sub is 0xA0 or 0xA1)
                    {
                        _host.SetMusicVolume(GetWork(2), GetWork(4));
                        _pc += 6;
                    }
                    else
                    {
                        _host.OnSkippedOpcode(op, _pc);
                        EndRequest();
                    }

                    return;
                }

                case 0x5D:
                    _host.SetMusicVolume(GetWork(1), GetWork(3));
                    _pc += 5;
                    return;

                case 0x69:
                    _host.SetSoundVolume(GetWork(2), Code8(1) != 0 ? 0f : 1f, 0);
                    _pc += 4;
                    return;

                case 0x6A:
                    _host.SetSoundVolume(GetWork(5), GetWork(1) * 0.001f, GetWork(3));
                    _pc += 7;
                    return;

                case 0x9A:
                    // Yields every frame; steps on once the music is ready.
                    _retFlag = true;
                    if (_host.IsMusicReady)
                    {
                        _pc++;
                    }

                    return;
            }
        }
    }
}
