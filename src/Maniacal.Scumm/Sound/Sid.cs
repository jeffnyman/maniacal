namespace Maniacal.Scumm.Sound;

/// <summary>
/// The C64's sound chip, the MOS 6581 SID: three voices of a waveform and an envelope each, a
/// filter, and the external filter of the C64's output stage, clocked cycle by cycle and sampled.
/// Mirrors ScummVM's <c>Resid::SID</c> (<c>audio/softsynth/sid.cpp</c>), based on reSID by Dag Lem.
/// </summary>
internal sealed class Sid
{
    // the NTSC clock and the cycles of a frame (timingProps: 263 lines of 65 cycles at 14.31818 MHz / 14)
    public const double NtscClock = 14318180.0 / 14;
    public const int NtscCyclesPerFrame = 263 * 65;

    private const int FixpShift = 16, FixpMask = 0xFFFF;

    private readonly Voice[] _voices = [new(), new(), new()];
    private readonly Filter _filter = new();
    private readonly ExternalFilter _external = new();
    private int _cyclesPerSample, _sampleOffset;
    private int _cyclesLeft;

    /// <summary>Mirrors the constructor and <c>init</c>: the voices sync in a ring, sampled at a rate.</summary>
    public Sid(int sampleRate)
    {
        _voices[0].Wave.SetSyncSource(_voices[2].Wave);
        _voices[1].Wave.SetSyncSource(_voices[0].Wave);
        _voices[2].Wave.SetSyncSource(_voices[1].Wave);
        SetSamplingParameters(NtscClock, sampleRate);
        Reset();
    }

    /// <summary>Mirrors <c>reset</c>.</summary>
    public void Reset()
    {
        foreach (var voice in _voices)
        {
            voice.Reset();
        }

        _filter.Reset();
        _external.Reset();
    }

    /// <summary>Writes one of the 25 registers. Mirrors <c>writeReg</c>, without the bus value only reads see.</summary>
    public void Write(int register, int value)
    {
        if (register < 0x15)
        {
            var voice = _voices[register / 7];
            switch (register % 7)
            {
                case 0:
                    voice.Wave.WriteFreqLo(value);
                    break;
                case 1:
                    voice.Wave.WriteFreqHi(value);
                    break;
                case 2:
                    voice.Wave.WritePwLo(value);
                    break;
                case 3:
                    voice.Wave.WritePwHi(value);
                    break;
                case 4:
                    voice.Wave.WriteControl(value);
                    voice.Envelope.WriteControl(value);
                    break;
                case 5:
                    voice.Envelope.WriteAttackDecay(value);
                    break;
                case 6:
                    voice.Envelope.WriteSustainRelease(value);
                    break;
            }

            return;
        }

        switch (register)
        {
            case 0x15:
                _filter.WriteFcLo(value);
                break;
            case 0x16:
                _filter.WriteFcHi(value);
                break;
            case 0x17:
                _filter.WriteResFilt(value);
                break;
            case 0x18:
                _filter.WriteModeVol(value);
                break;
        }
    }

    /// <summary>
    /// Fills a buffer with samples, a frame's cycles at a time. Mirrors <c>generateSamples</c> and
    /// the sampling <c>updateClock</c>.
    /// </summary>
    public void Generate(Span<short> buffer)
    {
        while (buffer.Length > 0)
        {
            if (_cyclesLeft <= 0)
            {
                _cyclesLeft = NtscCyclesPerFrame;
            }

            int count = Clock(ref _cyclesLeft, buffer);
            buffer = buffer[count..];
        }
    }

    private int Clock(ref int deltaT, Span<short> buffer)
    {
        int s = 0;
        while (true)
        {
            int nextSampleOffset = _sampleOffset + _cyclesPerSample + (1 << (FixpShift - 1));
            int deltaTSample = nextSampleOffset >> FixpShift;
            if (deltaTSample > deltaT)
            {
                break;
            }

            if (s >= buffer.Length)
            {
                return s;
            }

            Clock(deltaTSample);
            deltaT -= deltaTSample;
            _sampleOffset = (nextSampleOffset & FixpMask) - (1 << (FixpShift - 1));
            buffer[s++] = Output();
        }

        Clock(deltaT);
        _sampleOffset -= deltaT << FixpShift;
        deltaT = 0;
        return s;
    }

    private void SetSamplingParameters(double clock, double sampleRate)
    {
        // the passband ends at 20 kHz, or at 0.9 of half the sample rate below 44.4 kHz
        double pass = 20000;
        if (2 * pass / sampleRate >= 0.9)
        {
            pass = 0.9 * sampleRate / 2;
        }

        _external.SetSamplingParameter(pass);
        _cyclesPerSample = (int)(clock / sampleRate * (1 << FixpShift) + 0.5);
        _sampleOffset = 0;
    }

    /// <summary>Clocks the chip a number of cycles, the oscillators stopping at each sync source's MSB toggle. Mirrors <c>updateClock</c>.</summary>
    private void Clock(int deltaT)
    {
        if (deltaT <= 0)
        {
            return;
        }

        foreach (var voice in _voices)
        {
            voice.Envelope.Clock(deltaT);
        }

        int deltaTOsc = deltaT;
        while (deltaTOsc != 0)
        {
            int deltaTMin = deltaTOsc;
            foreach (var voice in _voices)
            {
                var wave = voice.Wave;
                if (!(wave.SyncDest!.Sync != 0 && wave.Freq != 0))
                {
                    continue;
                }

                uint deltaAccumulator = ((wave.Accumulator & 0x800000) != 0 ? 0x1000000u : 0x800000u) - wave.Accumulator;
                int deltaTNext = (int)(deltaAccumulator / wave.Freq);
                if (deltaAccumulator % wave.Freq != 0)
                {
                    deltaTNext++;
                }

                if (deltaTNext < deltaTMin)
                {
                    deltaTMin = deltaTNext;
                }
            }

            foreach (var voice in _voices)
            {
                voice.Wave.Clock(deltaTMin);
            }

            foreach (var voice in _voices)
            {
                voice.Wave.Synchronize();
            }

            deltaTOsc -= deltaTMin;
        }

        _filter.Clock(deltaT, _voices[0].Output(), _voices[1].Output(), _voices[2].Output());
        _external.Clock(deltaT, _filter.Output());
    }

    /// <summary>The external filter's output scaled to 16 bits. Mirrors <c>output</c>.</summary>
    private short Output()
    {
        const int Range = 1 << 16, Half = Range >> 1;
        int sample = _external.Output() / ((4095 * 255 >> 7) * 3 * 15 * 2 / Range);
        return (short)Math.Clamp(sample, -Half, Half - 1);
    }

    /// <summary>A voice: its waveform scaled by its envelope. Mirrors <c>Voice</c>.</summary>
    private sealed class Voice
    {
        private const int WaveZero = 0x380, VoiceDc = 0x800 * 0xFF;

        public WaveformGenerator Wave { get; } = new();

        public EnvelopeGenerator Envelope { get; } = new();

        public void Reset()
        {
            Wave.Reset();
            Envelope.Reset();
        }

        public int Output() => unchecked(((int)Wave.Output() - WaveZero) * Envelope.Counter + VoiceDc);
    }

    /// <summary>A voice's oscillator: a 24-bit accumulator and a noise shift register. Mirrors <c>WaveformGenerator</c>.</summary>
    private sealed class WaveformGenerator
    {
        private WaveformGenerator _syncSource;
        private bool _msbRising;
        private uint _shiftRegister;
        private uint _pw;
        private uint _waveform, _test, _ringMod;

        public WaveformGenerator()
        {
            _syncSource = this;
            Reset();
        }

        public uint Accumulator { get; private set; }

        public uint Freq { get; private set; }

        public uint Sync { get; private set; }

        public WaveformGenerator? SyncDest { get; private set; }

        public void SetSyncSource(WaveformGenerator source)
        {
            _syncSource = source;
            source.SyncDest = this;
        }

        public void WriteFreqLo(int value) => Freq = (Freq & 0xFF00) | ((uint)value & 0x00FF);

        public void WriteFreqHi(int value) => Freq = (((uint)value << 8) & 0xFF00) | (Freq & 0x00FF);

        public void WritePwLo(int value) => _pw = (_pw & 0xF00) | ((uint)value & 0x0FF);

        public void WritePwHi(int value) => _pw = (((uint)value << 8) & 0xF00) | (_pw & 0x0FF);

        public void WriteControl(int value)
        {
            uint control = (uint)value;
            _waveform = (control >> 4) & 0x0F;
            _ringMod = control & 0x04;
            Sync = control & 0x02;
            uint testNext = control & 0x08;
            if (testNext != 0)
            {
                Accumulator = 0;
                _shiftRegister = 0;
            }
            else if (_test != 0)
            {
                _shiftRegister = 0x7FFFF8;
            }

            _test = testNext;
        }

        public void Reset()
        {
            Accumulator = 0;
            _shiftRegister = 0x7FFFF8;
            Freq = 0;
            _pw = 0;
            _test = 0;
            _ringMod = 0;
            Sync = 0;
            _msbRising = false;
        }

        /// <summary>Mirrors <c>updateClock</c>: the accumulator adds the frequency each cycle, the noise register shifts on each rise of bit 19.</summary>
        public void Clock(int deltaT)
        {
            if (_test != 0)
            {
                return;
            }

            uint previous = Accumulator;
            uint deltaAccumulator = unchecked((uint)deltaT * Freq);
            Accumulator = unchecked(Accumulator + deltaAccumulator) & 0xFFFFFF;
            _msbRising = (previous & 0x800000) == 0 && (Accumulator & 0x800000) != 0;

            uint shiftPeriod = 0x100000;
            while (deltaAccumulator != 0)
            {
                if (deltaAccumulator < shiftPeriod)
                {
                    shiftPeriod = deltaAccumulator;
                    if (shiftPeriod <= 0x080000)
                    {
                        if ((unchecked(Accumulator - shiftPeriod) & 0x080000) != 0 || (Accumulator & 0x080000) == 0)
                        {
                            break;
                        }
                    }
                    else if ((unchecked(Accumulator - shiftPeriod) & 0x080000) != 0 && (Accumulator & 0x080000) == 0)
                    {
                        break;
                    }
                }

                uint bit0 = ((_shiftRegister >> 22) ^ (_shiftRegister >> 17)) & 0x1;
                _shiftRegister = ((_shiftRegister << 1) & 0x7FFFFF) | bit0;
                deltaAccumulator -= shiftPeriod;
            }
        }

        /// <summary>Mirrors <c>synchronize</c>: hard sync resets the voice this one syncs.</summary>
        public void Synchronize()
        {
            if (_msbRising && SyncDest!.Sync != 0 && !(Sync != 0 && _syncSource._msbRising))
            {
                SyncDest.Accumulator = 0;
            }
        }

        /// <summary>The 12-bit output of the selected waveforms. Mirrors <c>output</c>; with noise and another waveform the 6581 gives 0.</summary>
        public uint Output() => _waveform switch
        {
            0x1 => Triangle(),
            0x2 => Sawtooth(),
            0x3 => (uint)SidTables.Wave6581ST[(int)Sawtooth()] << 4,
            0x4 => Pulse(),
            0x5 => ((uint)SidTables.Wave6581PT[(int)(Triangle() >> 1)] << 4) & Pulse(),
            0x6 => ((uint)SidTables.Wave6581PS[(int)Sawtooth()] << 4) & Pulse(),
            0x7 => ((uint)SidTables.Wave6581PST[(int)Sawtooth()] << 4) & Pulse(),
            0x8 => Noise(),
            _ => 0,
        };

        private uint Triangle()
        {
            uint msb = (_ringMod != 0 ? Accumulator ^ _syncSource.Accumulator : Accumulator) & 0x800000;
            return ((msb != 0 ? ~Accumulator : Accumulator) >> 11) & 0xFFF;
        }

        private uint Sawtooth() => Accumulator >> 12;

        private uint Pulse() => _test != 0 || (Accumulator >> 12) >= _pw ? 0xFFFu : 0x000u;

        private uint Noise() =>
            ((_shiftRegister & 0x400000) >> 11) |
            ((_shiftRegister & 0x100000) >> 10) |
            ((_shiftRegister & 0x010000) >> 7) |
            ((_shiftRegister & 0x002000) >> 5) |
            ((_shiftRegister & 0x000800) >> 4) |
            ((_shiftRegister & 0x000080) >> 1) |
            ((_shiftRegister & 0x000010) << 1) |
            ((_shiftRegister & 0x000004) << 2);
    }

    /// <summary>A voice's ADSR envelope: an 8-bit counter stepped at a rate, slower at low levels. Mirrors <c>EnvelopeGenerator</c>.</summary>
    private sealed class EnvelopeGenerator
    {
        private enum State
        {
            Attack,
            DecaySustain,
            Release,
        }

        private int _rateCounter, _ratePeriod, _exponentialCounter, _exponentialCounterPeriod;
        private bool _holdZero;
        private int _attack, _decay, _sustain, _release, _gate;
        private State _state;

        public EnvelopeGenerator() => Reset();

        /// <summary>The envelope's level, 0 to 255. Mirrors <c>output</c>.</summary>
        public int Counter { get; private set; }

        public void Reset()
        {
            Counter = 0;
            _attack = _decay = _sustain = _release = 0;
            _gate = 0;
            _rateCounter = 0;
            _exponentialCounter = 0;
            _exponentialCounterPeriod = 1;
            _state = State.Release;
            _ratePeriod = SidTables.RateCounterPeriods[_release];
            _holdZero = true;
        }

        public void WriteControl(int control)
        {
            int gateNext = control & 0x01;
            if (_gate == 0 && gateNext != 0)
            {
                _state = State.Attack;
                _ratePeriod = SidTables.RateCounterPeriods[_attack];
                _holdZero = false;
            }
            else if (_gate != 0 && gateNext == 0)
            {
                _state = State.Release;
                _ratePeriod = SidTables.RateCounterPeriods[_release];
            }

            _gate = gateNext;
        }

        public void WriteAttackDecay(int value)
        {
            _attack = (value >> 4) & 0x0F;
            _decay = value & 0x0F;
            if (_state == State.Attack)
            {
                _ratePeriod = SidTables.RateCounterPeriods[_attack];
            }
            else if (_state == State.DecaySustain)
            {
                _ratePeriod = SidTables.RateCounterPeriods[_decay];
            }
        }

        public void WriteSustainRelease(int value)
        {
            _sustain = (value >> 4) & 0x0F;
            _release = value & 0x0F;
            if (_state == State.Release)
            {
                _ratePeriod = SidTables.RateCounterPeriods[_release];
            }
        }

        /// <summary>Mirrors <c>updateClock</c>, with the ADSR delay bug of a rate period set below the rate counter.</summary>
        public void Clock(int deltaT)
        {
            int rateStep = _ratePeriod - _rateCounter;
            if (rateStep <= 0)
            {
                rateStep += 0x7FFF;
            }

            while (deltaT != 0)
            {
                if (deltaT < rateStep)
                {
                    _rateCounter += deltaT;
                    if ((_rateCounter & 0x8000) != 0)
                    {
                        _rateCounter = (_rateCounter + 1) & 0x7FFF;
                    }

                    return;
                }

                _rateCounter = 0;
                deltaT -= rateStep;
                if (_state == State.Attack || ++_exponentialCounter == _exponentialCounterPeriod)
                {
                    _exponentialCounter = 0;
                    if (_holdZero)
                    {
                        rateStep = _ratePeriod;
                        continue;
                    }

                    switch (_state)
                    {
                        case State.Attack:
                            Counter = (Counter + 1) & 0xFF;
                            if (Counter == 0xFF)
                            {
                                _state = State.DecaySustain;
                                _ratePeriod = SidTables.RateCounterPeriods[_decay];
                            }

                            break;
                        case State.DecaySustain:
                            if (Counter != SidTables.SustainLevels[_sustain])
                            {
                                Counter--;
                            }

                            break;
                        case State.Release:
                            Counter = (Counter - 1) & 0xFF;
                            break;
                    }

                    switch (Counter)
                    {
                        case 0xFF:
                            _exponentialCounterPeriod = 1;
                            break;
                        case 0x5D:
                            _exponentialCounterPeriod = 2;
                            break;
                        case 0x36:
                            _exponentialCounterPeriod = 4;
                            break;
                        case 0x1A:
                            _exponentialCounterPeriod = 8;
                            break;
                        case 0x0E:
                            _exponentialCounterPeriod = 16;
                            break;
                        case 0x06:
                            _exponentialCounterPeriod = 30;
                            break;
                        case 0x00:
                            _exponentialCounterPeriod = 1;
                            _holdZero = true;
                            break;
                    }
                }

                rateStep = _ratePeriod;
            }
        }
    }

    /// <summary>The 6581's state-variable filter: high-, band- and low-pass. Mirrors <c>Filter</c>.</summary>
    private sealed class Filter
    {
        private const double Pi = 3.1415926535897932385;

        // the cutoff frequency for each value of the 11-bit FC register (f0_6581)
        private static readonly int[] CutoffFrequencies = BuildCutoffFrequencies();

        private int _fc, _res, _filt, _voice3Off, _hpBpLp, _vol;
        private int _vhp, _vbp, _vlp, _vnf;
        private int _w0CeilDt, _1024DivQ;
        private readonly int _mixerDc = (-0xFFF * 0xFF / 18) >> 7;

        public Filter() => Reset();

        public void Reset()
        {
            _fc = _res = _filt = _voice3Off = _hpBpLp = _vol = 0;
            _vhp = _vbp = _vlp = _vnf = 0;
            SetW0();
            SetQ();
        }

        public void WriteFcLo(int value)
        {
            _fc = (_fc & 0x7F8) | (value & 0x007);
            SetW0();
        }

        public void WriteFcHi(int value)
        {
            _fc = ((value << 3) & 0x7F8) | (_fc & 0x007);
            SetW0();
        }

        public void WriteResFilt(int value)
        {
            _res = (value >> 4) & 0x0F;
            SetQ();
            _filt = value & 0x0F;
        }

        public void WriteModeVol(int value)
        {
            _voice3Off = value & 0x80;
            _hpBpLp = (value >> 4) & 0x07;
            _vol = value & 0x0F;
        }

        /// <summary>Mirrors <c>updateClock</c>: the voices routed through or around the filter, which is stepped up to 8 cycles at a time.</summary>
        public void Clock(int deltaT, int voice1, int voice2, int voice3)
        {
            voice1 >>= 7;
            voice2 >>= 7;
            voice3 = _voice3Off != 0 && (_filt & 0x04) == 0 ? 0 : voice3 >> 7;

            // each voice goes through the filter (vi) or around it (vnf), as filt's low 3 bits say
            int vi = ((_filt & 1) != 0 ? voice1 : 0) + ((_filt & 2) != 0 ? voice2 : 0) + ((_filt & 4) != 0 ? voice3 : 0);
            _vnf = ((_filt & 1) == 0 ? voice1 : 0) + ((_filt & 2) == 0 ? voice2 : 0) + ((_filt & 4) == 0 ? voice3 : 0);

            int deltaTFlt = 8;
            while (deltaT != 0)
            {
                if (deltaT < deltaTFlt)
                {
                    deltaTFlt = deltaT;
                }

                unchecked
                {
                    int w0DeltaT = _w0CeilDt * deltaTFlt >> 6;
                    int dVbp = w0DeltaT * _vhp >> 14;
                    int dVlp = w0DeltaT * _vbp >> 14;
                    _vbp -= dVbp;
                    _vlp -= dVlp;
                    _vhp = (_vbp * _1024DivQ >> 10) - _vlp - vi;
                }

                deltaT -= deltaTFlt;
            }
        }

        /// <summary>The unfiltered voices and the chosen filter outputs, times the volume. Mirrors <c>output</c>.</summary>
        public int Output()
        {
            int vf = ((_hpBpLp & 1) != 0 ? _vlp : 0) + ((_hpBpLp & 2) != 0 ? _vbp : 0) + ((_hpBpLp & 4) != 0 ? _vhp : 0);
            return unchecked((_vnf + vf + _mixerDc) * _vol);
        }

        /// <summary>The cutoff, limited to 4 kHz to keep the filter stable when stepped several cycles at once. Mirrors <c>set_w0</c>.</summary>
        private void SetW0()
        {
            int w0 = (int)(2 * Pi * CutoffFrequencies[_fc] * 1.048576);
            int w0MaxDt = (int)(2 * Pi * 4000 * 1.048576);
            _w0CeilDt = w0 <= w0MaxDt ? w0 : w0MaxDt;
        }

        private void SetQ() => _1024DivQ = (int)(1024.0 / (0.707 + 1.0 * _res / 0x0F));

        /// <summary>
        /// The curve through the 6581's cutoff points, by reSID's spline: each segment a cubic with
        /// Catmull-Rom-like slopes, plotted by forward differencing. Mirrors <c>interpolate</c>
        /// with <c>PointPlotter</c>.
        /// </summary>
        private static int[] BuildCutoffFrequencies()
        {
            var f = new int[2048];
            var points = SidTables.CutoffPoints6581.ToArray();
            int count = points.Length / 2;
            double X(int i) => points[2 * i];
            double Y(int i) => points[2 * i + 1];

            // the last point is pn, which the loop stops at; p0 to p3 slide along
            for (int p0 = 0; p0 + 2 != count - 1; p0++)
            {
                int p1 = p0 + 1, p2 = p0 + 2, p3 = p0 + 3;
                if (X(p1) == X(p2))
                {
                    continue;
                }

                double k1, k2;
                if (X(p0) == X(p1) && X(p2) == X(p3))
                {
                    k1 = k2 = (Y(p2) - Y(p1)) / (X(p2) - X(p1));
                }
                else if (X(p0) == X(p1))
                {
                    k2 = (Y(p3) - Y(p1)) / (X(p3) - X(p1));
                    k1 = (3 * (Y(p2) - Y(p1)) / (X(p2) - X(p1)) - k2) / 2;
                }
                else if (X(p2) == X(p3))
                {
                    k1 = (Y(p2) - Y(p0)) / (X(p2) - X(p0));
                    k2 = (3 * (Y(p2) - Y(p1)) / (X(p2) - X(p1)) - k1) / 2;
                }
                else
                {
                    k1 = (Y(p2) - Y(p0)) / (X(p2) - X(p0));
                    k2 = (Y(p3) - Y(p1)) / (X(p3) - X(p1));
                }

                InterpolateSegment(f, X(p1), Y(p1), X(p2), Y(p2), k1, k2, 1.0);
            }

            return f;
        }

        private static void InterpolateSegment(int[] f, double x1, double y1, double x2, double y2, double k1, double k2, double res)
        {
            // cubic_coefficients
            double dx = x2 - x1, dy = y2 - y1;
            double a = ((k1 + k2) - 2 * dy / dx) / (dx * dx);
            double b = ((k2 - k1) / dx - 3 * (x1 + x2) * a) / 2;
            double c = k1 - (3 * x1 * a + 2 * b) * x1;
            double d = y1 - ((x1 * a + b) * x1 + c) * x1;

            double y = ((a * x1 + b) * x1 + c) * x1 + d;
            double dyStep = (3 * a * (x1 + res) + 2 * b) * x1 * res + ((a * res + b) * res + c) * res;
            double d2y = (6 * a * (x1 + res) + 2 * b) * res * res;
            double d3y = 6 * a * res * res * res;
            for (double x = x1; x <= x2; x += res)
            {
                f[(int)x] = (int)(y < 0 ? 0 : y);
                y += dyStep;
                dyStep += d2y;
                d2y += d3y;
            }
        }
    }

    /// <summary>The C64's output stage: a low-pass and a DC-blocking high-pass. Mirrors <c>ExternalFilter</c>.</summary>
    private sealed class ExternalFilter
    {
        private const double Pi = 3.1415926535897932385;

        private int _vlp, _vhp, _vo;
        private int _w0lp, _w0hp;

        public ExternalFilter()
        {
            Reset();
            SetSamplingParameter(15915.6);
        }

        public void SetSamplingParameter(double passFrequency)
        {
            _w0hp = 105;
            _w0lp = (int)(passFrequency * (2.0 * Pi * 1.048576));
            if (_w0lp > 104858)
            {
                _w0lp = 104858;
            }
        }

        public void Reset()
        {
            _vlp = 0;
            _vhp = 0;
            _vo = 0;
        }

        public void Clock(int deltaT, int vi)
        {
            int deltaTFlt = 8;
            while (deltaT != 0)
            {
                if (deltaT < deltaTFlt)
                {
                    deltaTFlt = deltaT;
                }

                unchecked
                {
                    int dVlp = (_w0lp * deltaTFlt >> 8) * (vi - _vlp) >> 12;
                    int dVhp = _w0hp * deltaTFlt * (_vlp - _vhp) >> 20;
                    _vo = _vlp - _vhp;
                    _vlp += dVlp;
                    _vhp += dVhp;
                }

                deltaT -= deltaTFlt;
            }
        }

        public int Output() => _vo;
    }
}
