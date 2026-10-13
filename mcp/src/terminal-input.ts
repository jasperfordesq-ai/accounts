/**
 * Reading a secret (a password, an authenticator code) at a terminal.
 *
 * No readline interface is open while a secret is typed: one created with
 * `terminal: true` echoes every keystroke it hears to its output, which is
 * how a hidden prompt ends up showing the password. The terminal is put in
 * raw mode so the operating system does not echo either, and the bytes are
 * collected by a listener that is removed afterwards. The stream is paused,
 * never destroyed (as leaving a `for await` over it would), so the next
 * prompt, such as the authenticator code after the password, can still read.
 */

/** The parts of process.stdin a hidden prompt uses; a fake TTY in tests. */
export interface SecretInput {
  isRaw?: boolean;
  setRawMode?: (mode: boolean) => unknown;
  on(event: 'data', listener: (chunk: Buffer | string) => void): unknown;
  on(event: 'end', listener: () => void): unknown;
  removeListener(event: 'data', listener: (chunk: Buffer | string) => void): unknown;
  removeListener(event: 'end', listener: () => void): unknown;
  resume(): unknown;
  pause(): unknown;
  unshift?(chunk: Buffer): unknown;
}

export interface SecretOutput {
  write(text: string): unknown;
}

export class PromptCancelledError extends Error {
  constructor() {
    super('Cancelled.');
    this.name = 'PromptCancelledError';
  }
}

const CTRL_C = 0x03;
const CTRL_D = 0x04;
const BACKSPACE = 0x08;
const LINE_FEED = 0x0a;
const CARRIAGE_RETURN = 0x0d;
const DELETE = 0x7f;

/** Removes the last whole UTF-8 character, never half of one. */
function dropLastCharacter(bytes: number[]): void {
  while (bytes.length > 0 && (bytes[bytes.length - 1]! & 0xc0) === 0x80) bytes.pop();
  bytes.pop();
}

/**
 * Asks `question` and reads one line without echoing it. Enter ends the
 * line; Backspace removes a character; Ctrl-C cancels. The bytes are decoded
 * once, at the end, so a multi-byte character split across reads survives.
 */
export function readSecret(input: SecretInput, output: SecretOutput, question: string): Promise<string> {
  output.write(question);
  const wasRaw = input.isRaw === true;
  input.setRawMode?.(true);

  return new Promise<string>((resolve, reject) => {
    const bytes: number[] = [];
    let settled = false;

    const finish = (error: Error | null, rest?: Buffer) => {
      if (settled) return;
      settled = true;
      input.removeListener('data', onData);
      input.removeListener('end', onEnd);
      input.pause();
      input.setRawMode?.(wasRaw);
      // Keystrokes typed ahead belong to whatever is asked next.
      if (rest && rest.length > 0) input.unshift?.(rest);
      output.write('\n');
      if (error) reject(error);
      else resolve(Buffer.from(bytes).toString('utf8'));
    };

    const onData = (chunk: Buffer | string) => {
      const buffer = typeof chunk === 'string' ? Buffer.from(chunk, 'utf8') : chunk;
      for (let index = 0; index < buffer.length; index += 1) {
        const byte = buffer[index]!;
        if (byte === CARRIAGE_RETURN || byte === LINE_FEED) {
          const skip = byte === CARRIAGE_RETURN && buffer[index + 1] === LINE_FEED ? 2 : 1;
          finish(null, buffer.subarray(index + skip));
          return;
        }
        if (byte === CTRL_C) {
          finish(new PromptCancelledError());
          return;
        }
        if (byte === CTRL_D && bytes.length === 0) {
          finish(new PromptCancelledError());
          return;
        }
        if (byte === DELETE || byte === BACKSPACE) {
          dropLastCharacter(bytes);
          continue;
        }
        bytes.push(byte);
      }
    };

    const onEnd = () => finish(new PromptCancelledError());

    input.on('data', onData);
    input.on('end', onEnd);
    input.resume();
  });
}
