namespace Maniacal.Scumm.Sound;

/// <summary>
/// The NES's sound chip as ScummVM emulates it for the NES release: two square channels, a
/// triangle and a noise channel, their length counters, envelopes and sweeps clocked by the frame
/// sequencer, mixed into samples at a rate. Mirrors <c>APUe</c> in <c>player_nes.cpp</c>, its
/// integer widths kept, so its wrap-arounds are the original's.
/// </summary>
internal sealed class NesApu
{
    // the NES's CPU clock, which the chip counts in, and the frame sequencer's period in it
    private const int CpuClock = 1789773, FramePeriod = 7457;

    private readonly int _sampleRate;
    private int _bufPos;
    private uint _frameCycles;
    private int _frameNumber;
    private readonly Square _square0 = new(), _square1 = new();
    private readonly Triangle _triangle = new();
    private readonly Noise _noise = new();

    public NesApu(int sampleRate)
    {
        _sampleRate = sampleRate;
        Reset();
    }

    /// <summary>Mirrors <c>APU::Reset</c>.</summary>
    public void Reset()
    {
        _bufPos = 0;
        _square0.Reset();
        _square1.Reset();
        _triangle.Reset();
        _noise.Reset();
        _frameNumber = 0;
        _frameCycles = 1;
    }

    /// <summary>Writes a register, 0x00 to 0x0F of the channels or 0x15 the channels' enables. Mirrors <c>APU::WriteReg</c>.</summary>
    public void WriteRegister(int address, byte value)
    {
        switch (address)
        {
            case >= 0x0 and <= 0x3:
                _square0.Write(address, value);
                break;
            case >= 0x4 and <= 0x7:
                _square1.Write(address - 4, value);
                break;
            case >= 0x8 and <= 0xB:
                _triangle.Write(address - 8, value);
                break;
            case >= 0xC and <= 0xF:
                _noise.Write(address - 0xC, value);
                break;
            case 0x15:
                _square0.Write(4, (byte)(value & 0x1));
                _square1.Write(4, (byte)(value & 0x2));
                _triangle.Write(4, (byte)(value & 0x4));
                _noise.Write(4, (byte)(value & 0x8));
                break;
        }
    }

    /// <summary>Which channels' length counters run, a bit each. Mirrors <c>APU::Read4015</c>.</summary>
    public byte ReadStatus()
        => (byte)((_square0.Timer != 0 ? 0x01 : 0) | (_square1.Timer != 0 ? 0x02 : 0) | (_triangle.Timer != 0 ? 0x04 : 0) | (_noise.Timer != 0 ? 0x08 : 0));

    /// <summary>The next sample: the channels' levels averaged over the CPU cycles it spans. Mirrors <c>APU::GetSample</c>.</summary>
    public short GetSample()
    {
        int sampleCycles = 1 + (CpuClock - _bufPos - 1) / _sampleRate;
        _bufPos = _bufPos + sampleCycles * _sampleRate - CpuClock;

        int level = Step(_square0, sampleCycles, _frameCycles, _frameNumber)
            + Step(_square1, sampleCycles, _frameCycles, _frameNumber)
            + Step(_triangle, sampleCycles, _frameCycles, _frameNumber)
            + Step(_noise, sampleCycles, _frameCycles, _frameNumber);

        uint left = (uint)sampleCycles;
        while (left >= _frameCycles)
        {
            left -= _frameCycles;
            _frameCycles = FramePeriod;
            if (_frameNumber < 4)
            {
                if ((_frameNumber & 1) != 0)
                {
                    _frameCycles++;
                }

                _frameNumber++;
            }
            else
            {
                _frameNumber = 0;
            }
        }

        _frameCycles -= left;
        return (short)((level << 6) / sampleCycles);
    }

    /// <summary>
    /// Runs a channel over a sample's cycles, the frame sequencer's quarter and half frames
    /// clocking it on the way, and sums its level over them. Mirrors the <c>step</c> template.
    /// </summary>
    private static int Step(Channel channel, int sampleCycles, uint frameCycles, int frameNumber)
    {
        int level = 0;
        uint cycles = (uint)sampleCycles;
        while (cycles != 0)
        {
            uint most = Math.Min(Math.Min(cycles, frameCycles), channel.Cycles);
            level += channel.Pos * (int)(most - 1);
            cycles -= most;
            frameCycles -= most;
            channel.Cycles -= most;

            if (frameCycles == 0)
            {
                frameCycles = FramePeriod;
                if (frameNumber < 4)
                {
                    channel.QuarterFrame();
                    if ((frameNumber & 1) != 0)
                    {
                        frameCycles++;
                    }
                    else
                    {
                        channel.HalfFrame();
                    }

                    frameNumber++;
                }
                else
                {
                    frameNumber = 0;
                }
            }

            if (channel.Cycles == 0)
            {
                channel.Run();
            }

            level += channel.Pos;
        }

        return level;
    }

    /// <summary>A channel's state as all have it. Mirrors <c>SoundGen</c>.</summary>
    private abstract class Channel
    {
        protected byte _waveHold;
        protected uint _freq;
        protected uint _curD = 1;

        public byte Timer;
        public int Pos;
        public uint Cycles = 1;

        public abstract void Reset();

        public abstract void Write(int register, byte value);

        public abstract void Run();

        public abstract void QuarterFrame();

        public abstract void HalfFrame();

        protected void ResetBase()
        {
            _waveHold = 0;
            _freq = 0;
            _curD = 1;
            Timer = 0;
            Pos = 0;
            Cycles = 1;
        }
    }

    /// <summary>A square channel: duty cycle, envelope, sweep. Mirrors <c>Square</c>.</summary>
    private sealed class Square : Channel
    {
        private byte _volume, _envelope, _duty, _sweepSpeed, _sweepDirection, _sweepStep, _sweepEnabled, _vol;
        private byte _envCounter = 1, _envelopeLevel, _bendCounter = 1;
        private bool _enabled, _validFreq, _active, _envClock, _sweepClock;

        public override void Reset()
        {
            ResetBase();
            _volume = _envelope = _duty = _sweepSpeed = _sweepDirection = _sweepStep = _sweepEnabled = _vol = 0;
            _envCounter = 1;
            _envelopeLevel = 0;
            _bendCounter = 1;
            _enabled = _validFreq = _active = _envClock = _sweepClock = false;
        }

        private void CheckActive()
        {
            _validFreq = _freq >= 0x8 && (_sweepDirection != 0 || ((_freq + (_freq >> _sweepStep)) & 0x800) == 0);
            _active = Timer != 0 && _validFreq;
            Pos = _active ? NesSoundTables.Duties[_duty * 8 + (int)_curD] * _vol : 0;
        }

        public override void Write(int register, byte value)
        {
            switch (register)
            {
                case 0:
                    _volume = (byte)(value & 0xF);
                    _envelope = (byte)(value & 0x10);
                    _waveHold = (byte)(value & 0x20);
                    _duty = (byte)((value >> 6) & 0x3);
                    _vol = _envelope != 0 ? _volume : _envelopeLevel;
                    break;
                case 1:
                    _sweepStep = (byte)(value & 0x07);
                    _sweepDirection = (byte)(value & 0x08);
                    _sweepSpeed = (byte)((value >> 4) & 0x7);
                    _sweepEnabled = (byte)(value & 0x80);
                    _sweepClock = true;
                    break;
                case 2:
                    _freq &= 0x700;
                    _freq |= value;
                    break;
                case 3:
                    _freq &= 0xFF;
                    _freq |= (uint)(value & 0x7) << 8;
                    if (_enabled)
                    {
                        Timer = NesSoundTables.LengthCounts[(value >> 3) & 0x1F];
                    }

                    _curD = 0;
                    _envClock = true;
                    break;
                case 4:
                    _enabled = value != 0;
                    if (!_enabled)
                    {
                        Timer = 0;
                    }

                    break;
            }

            CheckActive();
        }

        public override void Run()
        {
            Cycles = (_freq + 1) << 1;
            _curD = (_curD + 1) & 0x7;
            if (_active)
            {
                Pos = NesSoundTables.Duties[_duty * 8 + (int)_curD] * _vol;
            }
        }

        public override void QuarterFrame()
        {
            if (_envClock)
            {
                _envClock = false;
                _envelopeLevel = 0xF;
                _envCounter = (byte)(_volume + 1);
            }
            else if (--_envCounter == 0)
            {
                _envCounter = (byte)(_volume + 1);
                if (_envelopeLevel != 0)
                {
                    _envelopeLevel--;
                }
                else
                {
                    _envelopeLevel = (byte)(_waveHold != 0 ? 0xF : 0x0);
                }
            }

            _vol = _envelope != 0 ? _volume : _envelopeLevel;
            CheckActive();
        }

        public override void HalfFrame()
        {
            if (--_bendCounter == 0)
            {
                _bendCounter = (byte)(_sweepSpeed + 1);
                if (_sweepEnabled != 0 && _sweepStep != 0 && _validFreq)
                {
                    uint sweep = _freq >> _sweepStep;
                    _freq = _sweepDirection != 0 ? _freq - sweep : _freq + sweep;
                }
            }

            if (_sweepClock)
            {
                _sweepClock = false;
                _bendCounter = (byte)(_sweepSpeed + 1);
            }

            if (Timer != 0 && _waveHold == 0)
            {
                Timer--;
            }

            CheckActive();
        }
    }

    /// <summary>The triangle channel: its linear counter and 32 steps. Mirrors <c>Triangle</c>.</summary>
    private sealed class Triangle : Channel
    {
        private byte _linear, _linearCounter;
        private bool _enabled, _active, _linearClock;

        public override void Reset()
        {
            ResetBase();
            _linear = _linearCounter = 0;
            _enabled = _active = _linearClock = false;
        }

        private void CheckActive()
        {
            _active = Timer != 0 && _linearCounter != 0;

            // beyond hearing range
            Pos = _freq < 4 ? 0 : NesSoundTables.TriangleSteps[_curD] * 8;
        }

        public override void Write(int register, byte value)
        {
            switch (register)
            {
                case 0:
                    _linear = (byte)(value & 0x7F);
                    _waveHold = (byte)((value >> 7) & 0x1);
                    break;
                case 2:
                    _freq &= 0x700;
                    _freq |= value;
                    break;
                case 3:
                    _freq &= 0xFF;
                    _freq |= (uint)(value & 0x7) << 8;
                    if (_enabled)
                    {
                        Timer = NesSoundTables.LengthCounts[(value >> 3) & 0x1F];
                    }

                    _linearClock = true;
                    break;
                case 4:
                    _enabled = value != 0;
                    if (!_enabled)
                    {
                        Timer = 0;
                    }

                    break;
            }

            CheckActive();
        }

        public override void Run()
        {
            Cycles = _freq + 1;
            if (_active)
            {
                _curD = (_curD + 1) & 0x1F;
                Pos = _freq < 4 ? 0 : NesSoundTables.TriangleSteps[_curD] * 8;
            }
        }

        public override void QuarterFrame()
        {
            if (_linearClock)
            {
                _linearCounter = _linear;
            }
            else if (_linearCounter != 0)
            {
                _linearCounter--;
            }

            if (_waveHold == 0)
            {
                _linearClock = false;
            }

            CheckActive();
        }

        public override void HalfFrame()
        {
            if (Timer != 0 && _waveHold == 0)
            {
                Timer--;
            }

            CheckActive();
        }
    }

    /// <summary>The noise channel: its shift register, envelope and 16 periods. Mirrors <c>Noise</c>.</summary>
    private sealed class Noise : Channel
    {
        private byte _volume, _envelope, _dataType, _vol;
        private byte _envCounter = 1, _envelopeLevel;
        private bool _enabled, _envClock;

        public override void Reset()
        {
            ResetBase();
            _volume = _envelope = _dataType = _vol = 0;
            _envCounter = 1;
            _envelopeLevel = 0;
            _enabled = _envClock = false;
        }

        private int Level => ((_curD & 0x4000) != 0 ? -2 : 2) * _vol;

        public override void Write(int register, byte value)
        {
            switch (register)
            {
                case 0:
                    _volume = (byte)(value & 0x0F);
                    _envelope = (byte)(value & 0x10);
                    _waveHold = (byte)(value & 0x20);
                    _vol = _envelope != 0 ? _volume : _envelopeLevel;
                    if (Timer != 0)
                    {
                        Pos = Level;
                    }

                    break;
                case 2:
                    _freq = (uint)(value & 0xF);
                    _dataType = (byte)(value & 0x80);
                    break;
                case 3:
                    if (_enabled)
                    {
                        Timer = NesSoundTables.LengthCounts[(value >> 3) & 0x1F];
                    }

                    _envClock = true;
                    break;
                case 4:
                    _enabled = value != 0;
                    if (!_enabled)
                    {
                        Timer = 0;
                    }

                    break;
            }
        }

        public override void Run()
        {
            // no + 1 here
            Cycles = (uint)NesSoundTables.NoisePeriods[_freq];
            _curD = _dataType != 0
                ? (_curD << 1) | (((_curD >> 14) ^ (_curD >> 8)) & 0x1)
                : (_curD << 1) | (((_curD >> 14) ^ (_curD >> 13)) & 0x1);
            if (Timer != 0)
            {
                Pos = Level;
            }
        }

        public override void QuarterFrame()
        {
            if (_envClock)
            {
                _envClock = false;
                _envelopeLevel = 0xF;
                _envCounter = (byte)(_volume + 1);
            }
            else if (--_envCounter == 0)
            {
                _envCounter = (byte)(_volume + 1);
                if (_envelopeLevel != 0)
                {
                    _envelopeLevel--;
                }
                else
                {
                    _envelopeLevel = (byte)(_waveHold != 0 ? 0xF : 0x0);
                }
            }

            _vol = _envelope != 0 ? _volume : _envelopeLevel;
            if (Timer != 0)
            {
                Pos = Level;
            }
        }

        public override void HalfFrame()
        {
            if (Timer != 0 && _waveHold == 0)
            {
                Timer--;
            }
        }
    }
}
