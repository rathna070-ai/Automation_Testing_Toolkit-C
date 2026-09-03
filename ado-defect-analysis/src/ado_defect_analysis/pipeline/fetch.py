"""Phase 1: pull closed defects from Azure DevOps and land them in SQLite."""

from __future__ import annotations

import logging

from ..ado_client import AdoClient
from ..config import Config
from ..storage import DefectStore

logger = logging.getLogger(__name__)


def run_fetch(config: Config) -> int:
    """Returns the number of defects pulled and stored."""
    client = AdoClient(config.ado)
    store = DefectStore(config.db_path)

    defects = client.fetch_closed_defects()
    store.upsert_defects(defects)

    logger.info("Fetched and stored %d defects from Azure DevOps.", len(defects))
    return len(defects)
