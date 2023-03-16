import BaseHttpService from "./BaseHttpService";

class QueueService extends BaseHttpService {
  getQueuedBooks(): Promise<string[]> {
    return this.getData("/queue/books");
  }
}

export default new QueueService();
