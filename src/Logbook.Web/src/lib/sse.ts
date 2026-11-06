export interface ServerSentEvent {
  name: string
  data: string
}

// Reads a stream of server-sent events. EventSource cannot be used, as it cannot send the Authorization header, and the
// only alternative would be to put the admin key into the URL, where it ends up in the logs of every proxy.
export async function readServerSentEvents(body: ReadableStream<Uint8Array<ArrayBuffer>>, onEvent: (event: ServerSentEvent) => void) {
  const reader = body.pipeThrough(new TextDecoderStream()).getReader()

  let buffer = ''
  let name = ''
  let data: string[] = []

  while (true) {
    const { done, value } = await reader.read()

    if (done) {
      return
    }

    buffer += value

    const lines = buffer.split(/\r\n|\r|\n/)

    // The last piece is a line that is not finished yet, or empty.
    buffer = lines.pop() ?? ''

    for (const line of lines) {
      if (line === '') {
        if (data.length > 0) {
          onEvent({ name, data: data.join('\n') })
        }

        name = ''
        data = []
      } else if (line.startsWith('event:')) {
        name = line.slice('event:'.length).trimStart()
      } else if (line.startsWith('data:')) {
        data.push(line.slice('data:'.length).replace(/^ /, ''))
      }
    }
  }
}
