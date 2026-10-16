#!/bin/bash
# rundog.sh N [STRESS]: one Doom II level check of $EXE (default the Linux export) under gdb, which
# stops on SIGABRT/SIGSEGV and dumps the fault, the mappings and every thread; a run still going
# after $LIMIT s (default 900) gets SIGINT, so gdb dumps the hang too. STRESS sets ISODOOM_GC_STRESS.
# Log: $OUTDIR/N.log (default stress-runs/, gitignored as WAD-derived output). A dev tool (T8.3d).
# A batch: seq 1 150 | xargs -P 8 -I{} tools/Stress/rundog.sh {} 1
# then: grep -l "received signal\|WATCHDOG\|Handle is not" stress-runs/*.log
set -u
N=$1
cd "$(dirname "$0")/../.." || exit 1
EXE=${EXE:-$PWD/export/linux/IsoDoom.x86_64}
OUTDIR=${OUTDIR:-$PWD/stress-runs}
LIMIT=${LIMIT:-900}
IWAD=${IWAD:-$PWD/wads/doom2.wad}
mkdir -p "$OUTDIR"
export ISODOOM_GC_STRESS=${2:-}
# shellcheck disable=SC2016 # gdb's $_siginfo and $pc, not the shell's
gdb -q -batch -nx \
  -ex "set pagination off" -ex "set confirm off" \
  -ex "handle all nostop noprint pass" -ex "handle SIGABRT stop print" -ex "handle SIGSEGV stop print" -ex "handle SIGINT stop print nopass" \
  -ex run -ex 'p $_siginfo._sifields._sigfault.si_addr' -ex "info registers" -ex 'x/8i $pc' \
  -ex "info proc mappings" -ex "info threads" -ex "thread apply all bt 60" -ex kill \
  --args "$EXE" --headless -- --level-check -iwad "$IWAD" \
  > "$OUTDIR/$N.log" 2>&1 &
gdbpid=$!
for ((i = 0; i < LIMIT; i++)); do kill -0 $gdbpid 2>/dev/null || break; sleep 1; done
if kill -0 $gdbpid 2>/dev/null; then
  echo "WATCHDOG: still running after $LIMIT s" >> "$OUTDIR/$N.log"
  kill -INT $gdbpid
  for ((i = 0; i < 120; i++)); do kill -0 $gdbpid 2>/dev/null || break; sleep 1; done
  kill -9 $gdbpid 2>/dev/null
fi
wait $gdbpid
echo "run $N exit $? secs $SECONDS" >> "$OUTDIR/$N.log"
